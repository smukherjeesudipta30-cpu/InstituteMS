using FluentValidation;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.Holidays;

public record HolidayDto(
    int Id,
    DateOnly HolidayDate,
    string Name,
    string Category,
    bool IsInstituteHoliday,
    string? Note,
    string? ModifiedBy,
    DateTime? ModifiedOn);

/// <summary>
/// One year's list. <see cref="Source"/> says where it came from ("WB govt
/// notification", "Indian holiday calendar", "Copied from 2026", "Admin") and
/// is null when the year has never been loaded — only possible when the
/// calendar feed could not be reached.
/// </summary>
public record HolidayYearDto(int Year, string? Source, DateTime? LoadedOn, IReadOnlyList<HolidayDto> Holidays);

/// <summary>Adds or edits one holiday. Category defaults to "Institute" for a day Admin adds.</summary>
public record HolidaySaveRequest
{
    public DateOnly HolidayDate { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = HolidayCategories.Institute;
    public bool IsInstituteHoliday { get; init; } = true;
    public string? Note { get; init; }
}

public class HolidaySaveRequestValidator : AbstractValidator<HolidaySaveRequest>
{
    public HolidaySaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the holiday a name.").MaximumLength(150);
        RuleFor(x => x.HolidayDate).NotEqual(default(DateOnly)).WithMessage("Pick a date.");
        RuleFor(x => x.Category).Must(c => HolidayCategories.All.Contains(c))
            .WithMessage("Category must be National, State, Festival or Institute.");
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public interface IHolidayService
{
    /// <summary>
    /// One calendar year's holidays in date order. The first time a year is
    /// opened, its list is fetched automatically from the Indian holiday
    /// calendar and saved; after that it is only ever what Admin has made it.
    /// </summary>
    Task<HolidayYearDto> GetYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Admin's "fetch from calendar": adds any holiday the calendar has for that
    /// year that is not on the list yet (matched by name). Never changes or
    /// removes a day already there. Returns how many were added.
    /// </summary>
    Task<Result<int>> SyncFromCalendarAsync(int year, string? actor, CancellationToken ct = default);

    Task<Result<HolidayDto>> CreateAsync(HolidaySaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<HolidayDto>> UpdateAsync(int id, HolidaySaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Starts a new year from an earlier one: every holiday copied to the same
    /// day and month, keeping whether the institute closes, so Admin only has to
    /// correct the festival dates that move from year to year. A holiday whose
    /// name is already in the target year is skipped, so copying again never
    /// undoes a date Admin has corrected. Returns how many were added.
    /// </summary>
    Task<Result<int>> CopyYearAsync(int fromYear, int toYear, string? actor, CancellationToken ct = default);
}
