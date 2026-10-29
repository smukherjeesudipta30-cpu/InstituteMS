using InstituteMS.Application.Common;

namespace InstituteMS.Application.Offers;

/// <summary>The offer as Admin/Receptionist set it, with whether it is showing today.</summary>
public record OfferSettingsDto(
    int Percentage,
    DateOnly StartsOn,
    int Days,
    DateOnly EndsOn,
    bool IsShowing,
    string? ModifiedBy,
    DateTime? ModifiedOn);

public record OfferSaveRequest(int Percentage, DateOnly StartsOn, int Days);

/// <summary>What the public front page needs: the ribbon's figure and its last day.</summary>
public record OfferPublicDto(int Percentage, DateOnly EndsOn);

public interface IOfferService
{
    Task<OfferSettingsDto?> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<OfferSettingsDto>> SaveSettingsAsync(OfferSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>The offer if it is showing today, otherwise null.</summary>
    Task<OfferPublicDto?> GetPublicAsync(CancellationToken ct = default);
}
