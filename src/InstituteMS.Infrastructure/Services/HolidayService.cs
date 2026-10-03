using InstituteMS.Application.Common;
using InstituteMS.Application.Holidays;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The holiday list. 2026 comes from the West Bengal government notification
/// (db/migrations/075); any other year is fetched automatically from the Indian
/// holiday calendar the first time someone opens it. Each day is marked closed
/// or open for the institute. Everyone can read it; only Admin changes it.
/// </summary>
public class HolidayService : IHolidayService
{
    public const string SourceCalendar = "Indian holiday calendar";

    /// <summary>One fetch at a time, so two people opening a new year together cannot load it twice.</summary>
    private static readonly SemaphoreSlim ImportGate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly HolidayCalendarFeed _feed;

    public HolidayService(AppDbContext db, HolidayCalendarFeed feed)
    {
        _db = db;
        _feed = feed;
    }

    public async Task<HolidayYearDto> GetYearAsync(int year, CancellationToken ct = default)
    {
        var imported = await _db.HolidayYearImports.AsNoTracking().FirstOrDefaultAsync(i => i.Year == year, ct);

        // First visit to a year nobody has loaded: fetch it. A year already
        // loaded is never fetched again, so Admin's deletions stay deleted.
        if (imported is null && year is >= 2000 and <= 2100)
            imported = await ImportYearAsync(year, ct);

        var holidays = await ListYearAsync(year, ct);
        return new HolidayYearDto(year, imported?.Source, imported?.ImportedOn, holidays);
    }

    public async Task<IReadOnlyList<UpcomingClosureDto>> GetMonthClosedAsync(CancellationToken ct = default)
    {
        // The current month as a whole, past days included: the home page shows
        // this month's closures and greys out the ones already gone.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = new DateOnly(today.Year, today.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return await _db.Holidays.AsNoTracking()
            .Where(h => h.IsInstituteHoliday && h.HolidayDate >= start && h.HolidayDate <= end)
            .OrderBy(h => h.HolidayDate).ThenBy(h => h.Name)
            .Select(h => new UpcomingClosureDto(h.HolidayDate, h.Name))
            .ToListAsync(ct);
    }

    private async Task<HolidayYearImport?> ImportYearAsync(int year, CancellationToken ct)
    {
        await ImportGate.WaitAsync(ct);
        try
        {
            var existing = await _db.HolidayYearImports.AsNoTracking().FirstOrDefaultAsync(i => i.Year == year, ct);
            if (existing is not null) return existing;   // someone else loaded it while this request waited

            // Admin may have added or copied days into this year by hand before
            // anyone opened it — that list is the year's list, not a gap to fill.
            var start = new DateOnly(year, 1, 1);
            var end = new DateOnly(year, 12, 31);
            if (await _db.Holidays.AnyAsync(h => h.HolidayDate >= start && h.HolidayDate <= end, ct))
                return await RecordImportAsync(year, "Admin", 0, null, ct);

            var fetched = await _feed.FetchYearAsync(year, ct);
            if (fetched is null || fetched.Count == 0) return null;   // feed unreachable: try again next visit

            var added = await AddMissingAsync(year, fetched, "system (Indian holiday calendar)", ct);
            return await RecordImportAsync(year, SourceCalendar, added, null, ct);
        }
        finally
        {
            ImportGate.Release();
        }
    }

    public async Task<Result<int>> SyncFromCalendarAsync(int year, string? actor, CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100)
            return Result<int>.Failure("That year is out of range.", ResultStatus.BadRequest);

        var fetched = await _feed.FetchYearAsync(year, ct);
        if (fetched is null)
            return Result<int>.Failure("The holiday calendar could not be reached. Please try again later.", ResultStatus.BadRequest);
        if (fetched.Count == 0)
            return Result<int>.Failure($"The holiday calendar has nothing for {year} yet.", ResultStatus.NotFound);

        await ImportGate.WaitAsync(ct);
        try
        {
            var added = await AddMissingAsync(year, fetched, actor, ct);

            if (!await _db.HolidayYearImports.AnyAsync(i => i.Year == year, ct))
                await RecordImportAsync(year, SourceCalendar, added, actor, ct);

            return Result<int>.Success(added);
        }
        finally
        {
            ImportGate.Release();
        }
    }

    /// <summary>
    /// Adds each fetched holiday whose name is not already in that year. By
    /// name, not date: a festival Admin has moved to its correct date must not
    /// come back on the calendar's date.
    /// </summary>
    private async Task<int> AddMissingAsync(int year, IReadOnlyList<FeedHoliday> fetched, string? actor, CancellationToken ct)
    {
        var names = (await ListYearAsync(year, ct)).Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        var added = 0;

        foreach (var h in fetched)
        {
            if (!names.Add(h.Name)) continue;
            _db.Holidays.Add(new Holiday
            {
                HolidayDate = h.Date,
                Name = h.Name,
                Category = h.Category,
                IsInstituteHoliday = h.InstituteClosed,
                Note = h.Note,
                CreatedBy = actor,
                CreatedOn = now
            });
            added++;
        }

        await _db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<HolidayYearImport> RecordImportAsync(int year, string source, int added, string? actor, CancellationToken ct)
    {
        var row = new HolidayYearImport { Year = year, Source = source, Added = added, ImportedOn = DateTime.Now, ImportedBy = actor };
        _db.HolidayYearImports.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<IReadOnlyList<HolidayDto>> ListYearAsync(int year, CancellationToken ct)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        return await _db.Holidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
            .OrderBy(h => h.HolidayDate).ThenBy(h => h.Name)
            .Select(h => ToDto(h))
            .ToListAsync(ct);
    }

    public async Task<Result<HolidayDto>> CreateAsync(HolidaySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await _db.Holidays.AnyAsync(h => h.HolidayDate == request.HolidayDate && h.Name == name, ct))
            return Result<HolidayDto>.Failure($"'{name}' is already on the list for that date.", ResultStatus.Conflict);

        var holiday = new Holiday
        {
            HolidayDate = request.HolidayDate,
            Name = name,
            Category = request.Category,
            IsInstituteHoliday = request.IsInstituteHoliday,
            Note = Clean(request.Note),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync(ct);

        // A day added by hand to a year nobody has opened yet makes that the
        // year's list — it must not be topped up from the calendar later.
        if (!await _db.HolidayYearImports.AnyAsync(i => i.Year == request.HolidayDate.Year, ct))
            await RecordImportAsync(request.HolidayDate.Year, "Admin", 1, actor, ct);

        return Result<HolidayDto>.Success(ToDto(holiday));
    }

    public async Task<Result<HolidayDto>> UpdateAsync(int id, HolidaySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var holiday = await _db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holiday is null)
            return Result<HolidayDto>.Failure("That holiday no longer exists.", ResultStatus.NotFound);

        var name = request.Name.Trim();

        if (await _db.Holidays.AnyAsync(h => h.Id != id && h.HolidayDate == request.HolidayDate && h.Name == name, ct))
            return Result<HolidayDto>.Failure($"'{name}' is already on the list for that date.", ResultStatus.Conflict);

        holiday.HolidayDate = request.HolidayDate;
        holiday.Name = name;
        holiday.Category = request.Category;
        holiday.IsInstituteHoliday = request.IsInstituteHoliday;
        holiday.Note = Clean(request.Note);
        holiday.ModifiedBy = actor;
        holiday.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<HolidayDto>.Success(ToDto(holiday));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var holiday = await _db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holiday is null)
            return Result<bool>.Failure("That holiday no longer exists.", ResultStatus.NotFound);

        _db.Holidays.Remove(holiday);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> CopyYearAsync(int fromYear, int toYear, string? actor, CancellationToken ct = default)
    {
        if (fromYear == toYear)
            return Result<int>.Failure("Pick a different year to copy from.", ResultStatus.BadRequest);
        if (toYear is < 2000 or > 2100 || fromYear is < 2000 or > 2100)
            return Result<int>.Failure("That year is out of range.", ResultStatus.BadRequest);

        var source = await ListYearAsync(fromYear, ct);
        if (source.Count == 0)
            return Result<int>.Failure($"There are no holidays in {fromYear} to copy.", ResultStatus.NotFound);

        // Matched by name, not date: once Admin has moved a festival to its new
        // date, copying again must not bring it back on last year's date. (Names
        // that repeat within a year, like "Additional Durga Puja holiday", are
        // all copied — only names already in the target year are skipped.)
        var existingNames = (await ListYearAsync(toYear, ct))
            .Select(h => h.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var added = 0;
        foreach (var h in source)
        {
            if (existingNames.Contains(h.Name)) continue;

            // 29 February has no twin in a non-leap year — the 28th stands in.
            var day = Math.Min(h.HolidayDate.Day, DateTime.DaysInMonth(toYear, h.HolidayDate.Month));
            var date = new DateOnly(toYear, h.HolidayDate.Month, day);

            _db.Holidays.Add(new Holiday
            {
                HolidayDate = date,
                Name = h.Name,
                Category = h.Category,
                IsInstituteHoliday = h.IsInstituteHoliday,
                Note = h.Note,
                CreatedBy = actor,
                CreatedOn = now
            });
            added++;
        }

        await _db.SaveChangesAsync(ct);

        if (!await _db.HolidayYearImports.AnyAsync(i => i.Year == toYear, ct))
            await RecordImportAsync(toYear, $"Copied from {fromYear}", added, actor, ct);

        return Result<int>.Success(added);
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static HolidayDto ToDto(Holiday h) =>
        new(h.Id, h.HolidayDate, h.Name, h.Category, h.IsInstituteHoliday, h.Note, h.ModifiedBy ?? h.CreatedBy, h.ModifiedOn ?? h.CreatedOn);
}
