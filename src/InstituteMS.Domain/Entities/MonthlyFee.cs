namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.MonthlyFees</c> — the fee ledger, one row per installment paid.</summary>
public class MonthlyFee
{
    public int Id { get; set; }
    public string? StudentRegistrationId { get; set; }
    public string? BillNo { get; set; }
    /// <summary>The month the installment covers.</summary>
    public DateOnly? FeeDate { get; set; }

    /// <summary>
    /// The student's agreed monthly fee at the time of this payment. Needed because
    /// <c>Student.MonthlyFees</c> only holds the current figure, so a historic row
    /// cannot be interpreted after a fee change. Also shows how
    /// <see cref="Installment"/> is composed: agreed fee + late fine.
    /// </summary>
    public decimal? AgreedMonthlyFee { get; set; }
    /// <summary>
    /// The branch that took this payment, copied from the student when it was
    /// recorded. Stamped for the same reason as <see cref="AgreedMonthlyFee"/>:
    /// deriving it from the student's current branch would move every past payment
    /// the day a student transfers, silently rewriting last quarter's branch
    /// performance. Null on rows written before migration 030.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>The date the money was actually taken.</summary>
    public DateOnly? PaymentDate { get; set; }
    public decimal? Installment { get; set; }
    public decimal? OtherInstallment { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? OldDue { get; set; }
    public decimal? NewDue { get; set; }

    /// <summary>
    /// The UPI reference the student typed after paying against the institute's
    /// QR code. <c>null</c> on a payment taken at the counter, which is how an
    /// online payment is told apart from an offline one.
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// <c>Cash</c> or <c>Bank</c> — how the money physically arrived. Independent of
    /// <see cref="TransactionId"/>: a receptionist recording a UPI payment shown to
    /// them at the counter is still "Bank" even though no reference is captured for
    /// it the way a student's own online payment is. Null on rows written before
    /// migration 044, which predates the distinction.
    /// </summary>
    public string? PaymentMode { get; set; }

    /// <summary>
    /// When someone checked the reference against the Paytm account. Null means
    /// recorded but not yet checked — the state the dashboard lists, because the
    /// static QR gives the server no way to confirm the money arrived.
    /// </summary>
    public DateTime? VerifiedOn { get; set; }

    public string? VerifiedBy { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
