using InstituteMS.Application.Common;

namespace InstituteMS.Application.ExamFees;

/// <summary>One cell of the exam fee master: a course at a branch, and its fee (null = not set).</summary>
public record ExamFeeRateDto(int CourseId, string? CourseName, string Branch, decimal? Amount);

public record ExamFeeRatesSaveRequest
{
    public IReadOnlyList<ExamFeeRateDto> Rates { get; init; } = [];
}

public record ExamFeePaymentDto(
    int Id,
    DateOnly PaidOn,
    decimal Amount,
    string PaymentMode,
    string? Note,
    string? RecordedBy);

/// <summary>A student's exam fee: what is charged, what is paid, and what is still owed.</summary>
public record StudentExamFeeDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    int? CourseId,
    string? CourseName,
    string? Branch,
    /// <summary>The course/branch rate from the master; null when Admin has not set one.</summary>
    decimal? StandardFee,
    /// <summary>This student's own amount, when Admin has set one.</summary>
    decimal? OverrideFee,
    /// <summary>What this student is charged: the override if set, else the standard fee.</summary>
    decimal? ExamFee,
    decimal Paid,
    decimal Due,
    /// <summary>True once the student has been scheduled for, or has sat, an exam on the course.</summary>
    bool ExamScheduled,
    IReadOnlyList<ExamFeePaymentDto> Payments);

public record ExamFeeCollectRequest
{
    public decimal Amount { get; init; }
    public string PaymentMode { get; init; } = "Cash";
    public DateOnly? PaidOn { get; init; }
    public string? Note { get; init; }
}

/// <summary>Turns an exam fee typed into Other income into this student's exam fee payment.</summary>
public record ExamFeeMoveRequest
{
    public int OtherIncomeId { get; init; }
    public int StudentId { get; init; }
}

public record ExamFeeOverrideRequest
{
    /// <summary>Null clears the override, so the course/branch rate applies again.</summary>
    public decimal? Amount { get; init; }
}

/// <summary>A student who has been scheduled for an exam and still owes some of the exam fee.</summary>
public record ExamFeeDueRowDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    string? Phone,
    string? Branch,
    string? CourseName,
    decimal ExamFee,
    decimal Paid,
    decimal Due);

public interface IExamFeeService
{
    /// <summary>Every course at every branch, with the fee where one is set.</summary>
    Task<IReadOnlyList<ExamFeeRateDto>> GetRatesAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExamFeeRateDto>>> SaveRatesAsync(
        ExamFeeRatesSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<StudentExamFeeDto>> GetStudentAsync(int studentId, CancellationToken ct = default);

    /// <summary>The signed-in student's own exam fee.</summary>
    Task<Result<StudentExamFeeDto>> GetMineAsync(string email, CancellationToken ct = default);

    Task<Result<StudentExamFeeDto>> CollectAsync(
        int studentId, ExamFeeCollectRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default);

    /// <summary>
    /// Moves an exam fee that was typed into Other income onto a student as their
    /// exam fee payment (same date, amount, branch and person who received it) and
    /// removes it from Other income - so it is neither lost nor counted twice.
    /// </summary>
    Task<Result<StudentExamFeeDto>> MoveFromOtherIncomeAsync(
        ExamFeeMoveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<StudentExamFeeDto>> SetOverrideAsync(
        int studentId, ExamFeeOverrideRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Students scheduled for an exam who still owe exam fee; optionally one branch.</summary>
    Task<IReadOnlyList<ExamFeeDueRowDto>> GetDuesAsync(string? branch, CancellationToken ct = default, int? courseId = null);

    /// <summary>Of these registration ids, the ones whose exam fee is not fully paid.</summary>
    Task<HashSet<string>> UnpaidAmongAsync(IEnumerable<string> registrationIds, CancellationToken ct = default);
}
