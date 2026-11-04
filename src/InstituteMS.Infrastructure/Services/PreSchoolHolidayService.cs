using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The PreSchool's own holiday notices. Kept apart from the Computer Centre's
/// holiday list, which this never reads or writes.
/// </summary>
public class PreSchoolHolidayService : IPreSchoolHolidayService
{
    /// <summary>How far ahead the public page and parents are told about closures.</summary>
    private const int UpcomingDays = 60;

    private readonly AppDbContext _db;

    public PreSchoolHolidayService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PreSchoolHolidayDto>> ListAsync(int year, CancellationToken ct = default)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        return await _db.PreSchoolHolidays.AsNoTracking()
            .Where(h => h.FromDate <= end && h.ToDate >= start)
            .OrderBy(h => h.FromDate).ThenBy(h => h.Title)
            .Select(h => new PreSchoolHolidayDto(h.Id, h.FromDate, h.ToDate, h.Title, h.Note))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PreSchoolHolidayDto>> UpcomingAsync(CancellationToken ct = default)
    {
        // From the first of this month, not today: the notice shows the whole
        // month, with the days already gone marked as over, as the Computer
        // Centre's does.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = new DateOnly(today.Year, today.Month, 1);
        var until = today.AddDays(UpcomingDays);
        return await _db.PreSchoolHolidays.AsNoTracking()
            .Where(h => h.ToDate >= from && h.FromDate <= until)
            .OrderBy(h => h.FromDate).ThenBy(h => h.Title)
            .Select(h => new PreSchoolHolidayDto(h.Id, h.FromDate, h.ToDate, h.Title, h.Note))
            .ToListAsync(ct);
    }

    public async Task<Result<PreSchoolHolidayDto>> CreateAsync(PreSchoolHolidaySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = new PreSchoolHoliday { CreatedBy = actor, CreatedOn = DateTime.Now };
        Apply(entity, request);
        _db.PreSchoolHolidays.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result<PreSchoolHolidayDto>.Success(ToDto(entity));
    }

    public async Task<Result<PreSchoolHolidayDto>> UpdateAsync(int id, PreSchoolHolidaySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolHolidays.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (entity is null) return Result<PreSchoolHolidayDto>.Failure("That holiday no longer exists.", ResultStatus.NotFound);

        Apply(entity, request);
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Result<PreSchoolHolidayDto>.Success(ToDto(entity));
    }

    /// <summary>A holiday notice is only a notice — nothing points at it, so it can go.</summary>
    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var removed = await _db.PreSchoolHolidays.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0
            ? Result<bool>.Failure("That holiday no longer exists.", ResultStatus.NotFound)
            : Result<bool>.Success(true);
    }

    private static void Apply(PreSchoolHoliday entity, PreSchoolHolidaySaveRequest request)
    {
        entity.FromDate = request.FromDate;
        entity.ToDate = request.ToDate ?? request.FromDate;
        entity.Title = request.Title.Trim();
        entity.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    }

    private static PreSchoolHolidayDto ToDto(PreSchoolHoliday h) => new(h.Id, h.FromDate, h.ToDate, h.Title, h.Note);
}
