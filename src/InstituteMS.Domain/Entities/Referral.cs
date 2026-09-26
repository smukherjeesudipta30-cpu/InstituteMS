namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.ReferralSlab</c> — one admission-fee threshold and the
/// referral percentage from it upward. The highest slab not above the fee
/// applies; below the lowest there is no referral. Set by Admin.
/// </summary>
public class ReferralSlab
{
    public int Id { get; set; }
    public decimal MinFee { get; set; }
    public decimal Percent { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.ReferralCredit</c> — the referrer's share of one admission-fee
/// payment. A teacher's credits count towards their earnings; a receptionist's
/// are paid on their next monthly payment (<see cref="ReceptionistPaymentId"/>).
/// </summary>
public class ReferralCredit
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string? RegistrationId { get; set; }
    public int AdmissionFeePaymentId { get; set; }
    public string ReferrerType { get; set; } = string.Empty;
    public int ReferrerId { get; set; }
    public string? Branch { get; set; }
    public decimal PaymentAmount { get; set; }
    public decimal Percent { get; set; }
    public decimal Amount { get; set; }
    public DateOnly CreditedOn { get; set; }
    public int? ReceptionistPaymentId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}

public static class ReferrerTypes
{
    public const string Teacher = "Teacher";
    public const string Receptionist = "Receptionist";

    /// <summary>A direct / walk-in admission: recorded as deliberately unreferred, never credited.</summary>
    public const string None = "None";
}
