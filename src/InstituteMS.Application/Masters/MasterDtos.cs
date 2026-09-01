using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Masters;

/* ------------------------------------------------------------------ teachers */

public record TeacherListItemDto(
    int Id,
    string? Name,
    string? LoginId,
    string? Phone,
    /// <summary>Active students currently assigned to this teacher, in any of the three slots.</summary>
    int ActiveStudents,
    int TotalStudents,
    /// <summary>Everything ever paid to this teacher out of student fees.</summary>
    decimal TotalEarnings,
    DateOnly? LastPaidOn,
    /// <summary>
    /// True for the stand-in rows the migration created for teachers that had been
    /// deleted while students still referenced them.
    /// </summary>
    bool IsPlaceholder,
    /// <summary>False when the teacher can be removed without orphaning anything.</summary>
    bool IsReferenced);

public record TeacherSaveRequest
{
    public string? Name { get; init; }
    public string? LoginId { get; init; }
    public string? Phone { get; init; }
}

/// <summary>One line of a teacher's earnings (legacy <c>Teacher/Profits</c>, §4.4 T7).</summary>
public record TeacherEarningDto(
    int Id,
    DateOnly? PaidOn,
    string? BillNo,
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    decimal Amount);

public class TeacherQuery : PagedQuery
{
    /// <summary>Hide the migration's stand-in rows. Defaults to showing them.</summary>
    public bool IncludePlaceholders { get; set; } = true;
}

/* ------------------------------------------------------------------ branches */

public record BranchListItemDto(
    int Id,
    string? Name,
    /// <summary>Short code that goes into registration ids — Rajpur becomes <c>Raj</c>.</summary>
    string? Code,
    bool IsActive,
    /// <summary>Students recorded against this branch.</summary>
    int StudentCount,
    /// <summary>Registration ids already issued with this code; the code is then frozen.</summary>
    int IssuedRegistrations);

public record BranchSaveRequest
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
}

/* ----------------------------------------------------------------- contracts */

public interface ITeacherService
{
    Task<PagedResult<TeacherListItemDto>> GetListAsync(TeacherQuery query, CancellationToken ct = default);

    Task<Result<TeacherListItemDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Legacy <c>ufnGetTecChk</c> — true when the login id is already taken.</summary>
    Task<bool> LoginIdExistsAsync(string loginId, int? excludingTeacherId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(TeacherSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, TeacherSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Refuses to delete a teacher that any student or payout still references.</summary>
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    Task<PagedResult<TeacherEarningDto>> GetEarningsAsync(
        int teacherId, DateOnly? from, DateOnly? to, PagedQuery query, CancellationToken ct = default);

    /* ------------------------------- payments made to the teacher */

    /// <summary>
    /// Records money handed to a teacher. Writes <c>dbo.TecExpense</c>, which is
    /// the "paid" side of the teacher account report.
    /// </summary>
    Task<Result<int>> RecordPaymentAsync(
        TeacherPaymentRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Payments already made to one teacher, newest first.</summary>
    Task<Result<IReadOnlyList<TeacherPaymentDto>>> GetPaymentsAsync(
        int teacherId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>
    /// Earned, paid and pending for one teacher. Scoped to the same date range as
    /// the earnings list, so the three figures and the rows below them always
    /// describe the same period.
    /// </summary>
    Task<Result<TeacherAccountDto>> GetAccountAsync(
        int teacherId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Removes a payment recorded in error.</summary>
    Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default);
}

public interface IBranchService
{
    Task<IReadOnlyList<BranchListItemDto>> GetListAsync(CancellationToken ct = default);

    Task<Result<int>> CreateAsync(BranchSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, BranchSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> SetStatusAsync(int id, bool isActive, CancellationToken ct = default);
}

/* ============================================= payments made to a teacher */

/// <summary>
/// A payment handed to a teacher — their share of fees, actually paid out.
/// Writes <c>dbo.TecExpense</c>, which the teacher account report reads as
/// "paid".
/// </summary>
/// <remarks>
/// Until this existed the migrated <c>TecExpense</c> rows were read-only, so a
/// teacher's "paid" figure could never move and their pending balance only ever
/// grew. Recording a payment is an administrator's act alone.
/// </remarks>
public record TeacherPaymentRequest
{
    public int TeacherId { get; init; }
    public DateOnly? PaidOn { get; init; }
    public decimal Amount { get; init; }
}

public class TeacherPaymentRequestValidator : AbstractValidator<TeacherPaymentRequest>
{
    public TeacherPaymentRequestValidator()
    {
        RuleFor(x => x.TeacherId).GreaterThan(0).WithMessage("Choose a teacher.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Amount must be greater than zero.")
            .LessThan(1_000_000m).WithMessage("Amount must be less than 10,00,000.");

        // A date in the future is a typo, not a payment.
        RuleFor(x => x.PaidOn)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddDays(1)))
            .WithMessage("The payment date cannot be in the future.")
            .When(x => x.PaidOn.HasValue);
    }
}

/// <summary>One payment made to a teacher, as the master screen lists it.</summary>
public record TeacherPaymentDto(
    int Id,
    DateOnly? PaidOn,
    decimal Amount,
    string? RecordedBy,
    DateTime RecordedOn);

/// <summary>A teacher's account at a glance: earned, paid and the balance.</summary>
public record TeacherAccountDto(
    int TeacherId,
    string? TeacherName,
    decimal TotalEarned,
    decimal TotalPaid,
    /// <summary>Earned minus paid. Negative means the teacher has been overpaid.</summary>
    decimal Pending,

    /// <summary>The range these figures cover. Null on either side means all time.</summary>
    DateOnly? From = null,
    DateOnly? To = null,

    /// <summary>True when a date filter is applied, so the screen can say so.</summary>
    bool IsFiltered = false);
