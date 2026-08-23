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
    decimal OutstandingDue);

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
    DateOnly? NextDueDate);
