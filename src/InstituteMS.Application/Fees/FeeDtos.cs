using FluentValidation;

namespace InstituteMS.Application.Fees;

/// <summary>
/// The "Student's Admission details" header, plus everything the fee form needs to
/// calculate with. Replaces <c>Course/getStuDetails</c> + <c>MonthlyBill.GetMonthlyBill</c>.
/// </summary>
public record StudentFeeBillDto(
    int StudentId,
    string RegistrationId,
    string? StudentName,
    string? CourseName,
    int? CourseDuration,
    decimal? AdmissionFees,
    decimal? MonthlyFees,
    DateOnly? DateOfJoining,
    int? TeacherCount,
    int? PrimaryTeacherId,
    string? PrimaryTeacherName,
    int? SecondTeacherId,
    string? SecondTeacherName,
    int? ThirdTeacherId,
    string? ThirdTeacherName,
    /// <summary>Installments already recorded — the legacy "Payment done already ... Month".</summary>
    int InstallmentsPaid,
    /// <summary>Date of the most recent payment, or null if this is the first.</summary>
    DateOnly? LastPaymentDate,
    /// <summary>Latest <c>NewDue</c> carried forward.</summary>
    decimal OutstandingDue,

    /// <summary>
    /// The student's mobile number, so the fees screen can offer to send the
    /// receipt on WhatsApp without a second round trip to look the student up.
    /// </summary>
    string? Phone = null);

/// <summary>One row of the payment history (legacy <c>MonthlyDetail.MonthlyDetails</c>).</summary>
public record FeePaymentDto(
    int Id,
    string? RegistrationId,
    string? StudentName,
    string? CourseName,
    string? BillNo,
    DateOnly? FeeDate,
    DateOnly? PreviousPaymentDate,
    decimal? Installment,
    decimal? AmountPaid,
    decimal? Percentage,
    decimal? OldDue,
    decimal? NewDue,
    decimal? LateFine);

/// <summary>What the client asks the server to work out, and what it gets back.</summary>
public record FeeCalculationRequest(
    string RegistrationId,
    DateOnly FeeDate,
    decimal InstallmentAmount,
    decimal? AmountPaid);

public record FeeCalculationDto(
    /// <summary>Days between the fee date and the reference date (joining or last payment).</summary>
    int DaysSincePrevious,
    /// <summary>Days per outstanding month, used to decide the fine.</summary>
    decimal DaysPerInstallment,
    decimal LateFine,
    /// <summary>Installment + late fine.</summary>
    decimal TotalForMonth,
    decimal OldDue,
    /// <summary>(total + old due) − amount actually paid.</summary>
    decimal NewDue,
    DateOnly? ReferenceDate,
    string Explanation);

public record FeeSaveRequest
{
    public string RegistrationId { get; init; } = string.Empty;
    /// <summary>The month this payment covers (legacy <c>UpdateDate</c> / <c>Date1</c>).</summary>
    public DateOnly FeeDate { get; init; }
    /// <summary>Date of the previous payment (legacy <c>PDate</c>).</summary>
    public DateOnly? PreviousPaymentDate { get; init; }
    public decimal InstallmentAmount { get; init; }
    /// <summary>Teacher share, as a percentage of the installment.</summary>
    public decimal TeacherPercentage { get; init; }
    public decimal LateFine { get; init; }
    /// <summary>Installment + fine. Stored as <c>MonthlyFees.Installment</c>.</summary>
    public decimal TotalForMonth { get; init; }
    /// <summary>What the student actually handed over (legacy <c>OInstallment</c>).</summary>
    public decimal AmountPaid { get; init; }
    public decimal OldDue { get; init; }
    public decimal NewDue { get; init; }
}

public record FeeRecordedDto(int Id, string BillNo, decimal TeacherSalaryEach);

/// <summary>Money receipt (legacy <c>MonthlyCourseFees/PrintReport</c>).</summary>
public record FeeReceiptDto(
    int Id,
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    string? BillNo,
    decimal? AmountPaid,
    string AmountPaidInWords,
    decimal? OldDue,
    decimal? NewDue,
    DateOnly? FeeDate,
    DateOnly? NextDueDate,

    /// <summary>
    /// Absolute URL of the PDF, for the WhatsApp message. See AdmissionReceiptDto.
    /// </summary>
    string? PdfUrl = null);

/* ======================================= online payment from the portal */

/// <summary>
/// What the signed-in student owes for their next unpaid month, and everything
/// needed to pay it against the institute's UPI QR code.
/// </summary>
/// <remarks>
/// One month at a time — the next one due. The fee ledger advances a month per
/// payment, so offering several at once would need each to be dated and fined
/// separately.
/// </remarks>
public record MyFeeDueDto(
    string RegistrationId,
    string? StudentName,
    string? CourseName,

    /// <summary>
    /// The date the payment is being made — today. It is what the counter puts on a
    /// payment, and the fine is worked out from it, so the portal and the desk give
    /// the same answer for the same student on the same day.
    /// </summary>
    DateOnly FeeDate,

    /// <summary>
    /// When this month's fee fell due: one fee month after the previous payment.
    /// Shown to the student so a fine has a visible reason. Null for a student with
    /// no payment history, who cannot pay online anyway.
    /// </summary>
    DateOnly? DueDate,

    decimal MonthlyFee,
    decimal LateFine,
    /// <summary>Monthly fee plus the fine — the amount to transfer.</summary>
    decimal AmountPayable,

    decimal OldDue,

    /// <summary>Why the fine is what it is, in words, from the shared calculator.</summary>
    string? FineExplanation,

    DateOnly? PreviousPaymentDate,

    /// <summary>False when there is nothing to pay, with <see cref="Reason"/> saying why.</summary>
    bool CanPay,
    string? Reason,

    /// <summary>The institute's UPI id, shown beside the QR code.</summary>
    string? UpiId,
    string? PayeeName);

/// <summary>What the student submits after paying against the QR code.</summary>
public record OnlineFeePaymentRequest
{
    /// <summary>
    /// The UPI reference from the student's own app. The server cannot check it —
    /// the QR is static and there is no gateway callback — so the payment is
    /// recorded unverified and the office confirms it against the Paytm account.
    /// </summary>
    public string TransactionId { get; init; } = string.Empty;
}

public class OnlineFeePaymentRequestValidator : AbstractValidator<OnlineFeePaymentRequest>
{
    public OnlineFeePaymentRequestValidator()
    {
        RuleFor(x => x.TransactionId)
            .NotEmpty().WithMessage("Enter the UPI reference number shown in your payment app.")
            .MinimumLength(6).WithMessage("That reference looks too short to be a UPI reference.")
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9-]+$")
            .WithMessage("A UPI reference is letters and digits only.");
    }
}

/// <summary>One unverified online payment, as the dashboard lists it.</summary>
public record OnlinePaymentNoticeDto(
    int FeeId,
    string? BillNo,
    string? RegistrationId,
    string? StudentName,
    string? CourseName,

    /// <summary>The month paid for.</summary>
    DateOnly? FeeDate,

    decimal MonthlyFee,
    decimal LateFine,
    decimal AmountPaid,

    string? TransactionId,

    /// <summary>When the student submitted it.</summary>
    DateTime PaidOn);
