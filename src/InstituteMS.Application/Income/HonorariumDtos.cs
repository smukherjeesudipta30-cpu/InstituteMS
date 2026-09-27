using InstituteMS.Application.Common;

namespace InstituteMS.Application.Income;

/// <summary>One honorarium amount and the month it starts from. <see cref="Branch"/> null = the whole institute.</summary>
public record HonorariumEntryDto(
    int Id, string? Branch, DateOnly EffectiveFrom, decimal MonthlyAmount, string? Note, string? CreatedBy, DateTime CreatedOn);

/// <summary>The amount in force this month for one branch (or the whole institute when null).</summary>
public record HonorariumCurrentDto(string? Branch, decimal MonthlyAmount);

/// <summary>
/// What is in force now — per branch and in total — plus every change so far (newest first).
/// <see cref="CurrentMonthlyAmount"/> is the total across all branches and the institute-wide amount.
/// </summary>
public record HonorariumDto(
    decimal? CurrentMonthlyAmount, IReadOnlyList<HonorariumCurrentDto> CurrentByBranch, IReadOnlyList<HonorariumEntryDto> History);

/// <summary>Sets the monthly amount from a month on (any day in that month; 0 stops it). No branch = whole institute.</summary>
public record HonorariumSaveRequest(DateOnly EffectiveFrom, decimal MonthlyAmount, string? Note, string? Branch = null);

public interface IHonorariumService
{
    Task<HonorariumDto> GetAsync(CancellationToken ct = default);
    Task<Result<HonorariumDto>> SaveAsync(HonorariumSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<HonorariumDto>> DeleteAsync(int id, CancellationToken ct = default);
}
