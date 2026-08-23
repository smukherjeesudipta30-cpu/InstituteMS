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
    /// <summary>The date the money was actually taken.</summary>
    public DateOnly? PaymentDate { get; set; }
    public decimal? Installment { get; set; }
    public decimal? OtherInstallment { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? OldDue { get; set; }
    public decimal? NewDue { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
