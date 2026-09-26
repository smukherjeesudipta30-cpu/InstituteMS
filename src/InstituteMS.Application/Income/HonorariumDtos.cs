using InstituteMS.Application.Common;

namespace InstituteMS.Application.Income;

/// <summary>One honorarium amount and the month it starts from.</summary>
public record HonorariumEntryDto(int Id, DateOnly EffectiveFrom, decimal MonthlyAmount, string? Note, string? CreatedBy, DateTime CreatedOn);

/// <summary>The amount in force now, plus every change so far (newest first).</summary>
public record HonorariumDto(decimal? CurrentMonthlyAmount, IReadOnlyList<HonorariumEntryDto> History);

/// <summary>Sets the monthly amount from a month on (any day in that month; 0 stops it).</summary>
public record HonorariumSaveRequest(DateOnly EffectiveFrom, decimal MonthlyAmount, string? Note);

public interface IHonorariumService
{
    Task<HonorariumDto> GetAsync(CancellationToken ct = default);
    Task<Result<HonorariumDto>> SaveAsync(HonorariumSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<HonorariumDto>> DeleteAsync(int id, CancellationToken ct = default);
}
