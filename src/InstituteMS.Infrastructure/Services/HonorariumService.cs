using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>The proprietor's monthly honorarium: set by Admin, charged to one branch or to the whole institute.</summary>
public class HonorariumService : IHonorariumService
{
    private readonly AppDbContext _db;

    public HonorariumService(AppDbContext db) => _db = db;

    public async Task<HonorariumDto> GetAsync(CancellationToken ct = default)
    {
        var rows = await _db.ProprietorHonoraria.AsNoTracking()
            .OrderBy(h => h.Branch)
            .ThenByDescending(h => h.EffectiveFrom)
            .Select(h => new HonorariumEntryDto(h.Id, h.Branch, h.EffectiveFrom, h.MonthlyAmount, h.Note, h.CreatedBy, h.CreatedOn))
            .ToListAsync(ct);

        // Each branch (and the institute-wide line) runs its own timeline:
        // what is in force is that branch's latest row starting on or before now.
        var thisMonth = FirstOfMonth(DateOnly.FromDateTime(DateTime.Today));
        var current = rows
            .GroupBy(r => r.Branch)
            .Select(g => g.Where(r => r.EffectiveFrom <= thisMonth).MaxBy(r => r.EffectiveFrom))
            .Where(r => r is not null && r.MonthlyAmount > 0m)
            .Select(r => new HonorariumCurrentDto(r!.Branch, r.MonthlyAmount))
            .OrderBy(c => c.Branch is null ? 0 : 1).ThenBy(c => c.Branch)
            .ToList();

        decimal? total = current.Count == 0 ? null : current.Sum(c => c.MonthlyAmount);
        return new HonorariumDto(total, current, rows);
    }

    public async Task<Result<HonorariumDto>> SaveAsync(HonorariumSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.MonthlyAmount < 0m)
            return Result<HonorariumDto>.Failure("The honorarium cannot be negative.", ResultStatus.BadRequest);

        var branch = string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim();

        // Stored by name, the same key every other income figure is filtered on —
        // so it must be a real branch or the branch report would never find it.
        if (branch is not null && !await _db.Branches.AnyAsync(b => b.Name == branch, ct))
            return Result<HonorariumDto>.Failure($"There is no branch called '{branch}'.", ResultStatus.BadRequest);

        var month = FirstOfMonth(request.EffectiveFrom);

        // One amount per branch per starting month: saving the same one again corrects it.
        var row = await _db.ProprietorHonoraria
            .FirstOrDefaultAsync(h => h.EffectiveFrom == month && h.Branch == branch, ct);
        if (row is null)
        {
            row = new ProprietorHonorarium { EffectiveFrom = month, Branch = branch };
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
    /// <see cref="Allocate"/> for several independent timelines at once — one per
    /// branch plus the institute-wide one — summed month by month. A branch's new
    /// amount must not end another branch's, so each is allocated on its own.
    /// </summary>
    internal static List<(DateOnly Month, decimal Amount)> AllocateByBranch(
        IReadOnlyList<ProprietorHonorarium> rates, DateOnly from, DateOnly to) =>
        rates.GroupBy(r => r.Branch)
            .SelectMany(g => Allocate(g.ToList(), from, to))
            .GroupBy(m => m.Month)
            .Select(g => (g.Key, g.Sum(m => m.Amount)))
            .OrderBy(m => m.Key)
            .ToList();

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
