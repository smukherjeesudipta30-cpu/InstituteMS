using InstituteMS.Application.Common;

namespace InstituteMS.Application.Referrals;

/// <summary>One slab: from <see cref="MinFee"/> upward the referrer earns <see cref="Percent"/>.</summary>
public record ReferralSlabDto(decimal MinFee, decimal Percent);

public record ReferralSettingsDto(
    IReadOnlyList<ReferralSlabDto> Slabs,
    string? ModifiedBy,
    DateTime? ModifiedOn);

public record ReferralSettingsSaveRequest
{
    public IReadOnlyList<ReferralSlabDto> Slabs { get; init; } = [];
}

/// <summary>A teacher or receptionist who can be named as a student's referrer.</summary>
public record ReferrerOptionDto(string Type, int Id, string Name, string? Branch);

/// <summary>One referral credit, for listings.</summary>
public record ReferralCreditDto(
    int Id,
    DateOnly CreditedOn,
    string? RegistrationId,
    string? StudentName,
    string ReferrerType,
    int ReferrerId,
    string? ReferrerName,
    string? Branch,
    decimal PaymentAmount,
    decimal Percent,
    decimal Amount,
    bool PaidOnReceptionistPay);

public interface IReferralService
{
    Task<ReferralSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<Result<ReferralSettingsDto>> SaveSettingsAsync(
        ReferralSettingsSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Active teachers and receptionists, for the registration form's Reference dropdown.</summary>
    Task<IReadOnlyList<ReferrerOptionDto>> GetReferrersAsync(CancellationToken ct = default);

    /// <summary>The percentage an admission fee earns under the current slabs (0 below the lowest).</summary>
    Task<decimal> PercentForFeeAsync(decimal admissionFee, CancellationToken ct = default);

    /// <summary>Null when the referrer exists and is active; otherwise why not.</summary>
    Task<string?> ValidateReferrerAsync(string? type, int? id, CancellationToken ct = default);

    /// <summary>
    /// Records the referrer's share of one admission-fee payment (and saves it),
    /// then tells the referrer and Admin, SuperAdmin and Editor. Does nothing for
    /// a student with no referrer or no referral rate.
    /// </summary>
    Task CreditAdmissionPaymentAsync(int admissionFeePaymentId, string? actor, CancellationToken ct = default);

    Task<IReadOnlyList<ReferralCreditDto>> GetCreditsAsync(
        DateOnly? from, DateOnly? to, string? referrerType, int? referrerId, CancellationToken ct = default);
}
