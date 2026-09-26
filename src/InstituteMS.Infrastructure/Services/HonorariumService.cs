using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>The proprietor's monthly honorarium: set by Admin, a cost of the whole institute.</summary>
public class HonorariumService : IHonorariumService
{
    private readonly AppDbContext _db;

    public HonorariumService(AppDbContext db) => _db = db;

    public async Task<HonorariumDto> GetAsync(CancellationToken ct = default)
    {
        var rows = await _db.ProprietorHonoraria.AsNoTracking()
            .OrderByDescending(h => h.EffectiveFrom)
            .Select(h => new HonorariumEntryDto(h.Id, h.EffectiveFrom, h.MonthlyAmount, h.Note, h.CreatedBy, h.CreatedOn))
            .ToListAsync(ct);

        var thisMonth = FirstOfMonth(DateOnly.FromDateTime(DateTime.Today));
        var current = rows.FirstOrDefault(r => r.EffectiveFrom <= thisMonth)?.MonthlyAmount;
        return new HonorariumDto(current, rows);
    }

    public async Task<Result<HonorariumDto>> SaveAsync(HonorariumSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.MonthlyAmount < 0m)
            return Result<HonorariumDto>.Failure("The honorarium cannot be negative.", ResultStatus.BadRequest);

        var month = FirstOfMonth(request.EffectiveFrom);

        // One amount per starting month: saving the same month again corrects it.
        var row = await _db.ProprietorHonoraria.FirstOrDefaultAsync(h => h.EffectiveFrom == month, ct);
        if (row is null)
        {
            row = new ProprietorHonorarium { EffectiveFrom = month };
            _db.ProprietorHonoraria.Add(row);
        }

        row.MonthlyAmount = request.MonthlyAmount;
        row.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        row.CreatedBy = actor;
        row.CreatedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<HonorariumDto>.Success(await GetAsync(ct));
    }

    public async Task<Result<HonorariumDto>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.ProprietorHonoraria.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (row is null)
            return Result<HonorariumDto>.Failure("That honorarium entry no longer exists.", ResultStatus.NotFound);

        _db.ProprietorHonoraria.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Result<HonorariumDto>.Success(await GetAsync(ct));
    }

    internal static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>
    /// The honorarium falling in [from, to], one entry per calendar month touched.
    /// A month only partly inside the range is charged pro rata by days, so a
    /// 1-15 September report carries half of September's amount.
    /// </summary>
    internal static List<(DateOnly Month, decimal Amount)> Allocate(
        IReadOnlyList<ProprietorHonorarium> rates, DateOnly from, DateOnly to)
    {
        var result = new List<(DateOnly, decimal)>();
        if (to < from || rates.Count == 0) return result;

        for (var month = FirstOfMonth(from); month <= to; month = month.AddMonths(1))
        {
            var rate = rates.Where(r => r.EffectiveFrom <= month)
                .OrderByDescending(r => r.EffectiveFrom).FirstOrDefault()?.MonthlyAmount ?? 0m;
            if (rate == 0m) continue;

            var monthEnd = month.AddMonths(1).AddDays(-1);
            var start = from > month ? from : month;
            var end = to < monthEnd ? to : monthEnd;
            var days = end.DayNumber - start.DayNumber + 1;

            var amount = days == monthEnd.Day
                ? rate
                : decimal.Round(rate * days / monthEnd.Day, 2, MidpointRounding.AwayFromZero);
            result.Add((month, amount));
        }

        return result;
    }
}
