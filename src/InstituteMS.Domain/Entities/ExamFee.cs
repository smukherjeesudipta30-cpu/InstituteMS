namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.ExamFeeRate</c> — the exam fee Admin sets for one course at one
/// branch. A student's own <see cref="Student.ExamFeeOverride"/>, when set, wins.
/// </summary>
public class ExamFeeRate
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Branch { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.ExamFeePayment</c> — one exam fee collected from a student.
/// Charged once per course; the institute keeps all of it (no teacher share,
/// no referral commission).
/// </summary>
public class ExamFeePayment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string? RegistrationId { get; set; }
    public int? CourseId { get; set; }
    public string? Branch { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public DateOnly PaidOn { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
