namespace InstituteMS.Domain.Entities;

/// <summary>
/// One receipt against a student's admission fee — maps to
/// <c>dbo.AdmissionFeePayment</c>.
/// </summary>
/// <remarks>
/// <para>The admission fee used to be all-or-nothing: whatever went in
/// <c>Student.StuAdFees</c> was taken as received in full, with a single
/// <c>Cash</c>/<c>Bank</c> tag on it. A student paying 300 of 600 at the counter
/// and the rest a fortnight later had nowhere to be recorded.</para>
///
/// <para>A row per receipt rather than a second "balance paid" column, because the
/// two parts are genuinely separate events: taken on different days, possibly in
/// different ways — cash at the desk now, a bank transfer later — and the office
/// needs to be able to say which was which. <c>Student.AdmissionFeesPaid</c> is the
/// running total these rows add up to, kept alongside them so the dues report is a
/// column read rather than a sum over this table for every student.</para>
/// </remarks>
public class AdmissionFeePayment
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    /// <summary>
    /// Copied from the student at the time of the receipt. Denormalised on purpose:
    /// the registration id is what the office quotes and what reports group by, and
    /// this table is read alongside them.
    /// </summary>
    public string? RegistrationId { get; set; }

    public decimal Amount { get; set; }

    /// <summary><c>Cash</c> or <c>Bank</c>, as on every other payment here.</summary>
    public string? PaymentMode { get; set; }

    /// <summary>The day the money was taken, which is not always the day it was typed in.</summary>
    public DateOnly PaidOn { get; set; }

    /// <summary>
    /// True for the receipt written at admission itself, false for a later
    /// instalment against the balance. Only so a receipt can be read back without
    /// inferring it from being the earliest row.
    /// </summary>
    public bool IsAtAdmission { get; set; }

    public string? Note { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
