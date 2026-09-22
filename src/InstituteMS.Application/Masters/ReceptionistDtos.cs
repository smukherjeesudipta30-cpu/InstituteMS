using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Masters;

/* ================================================== receptionist master */

public class ReceptionistQuery : PagedQuery
{
    /// <summary>Name, user id or phone. Narrows <see cref="PagedQuery.Search"/>.</summary>
    public new string? Search { get; set; }

    /// <summary>Empty for every branch.</summary>
    public string? Branch { get; set; }

    /// <summary>False includes people who have left.</summary>
    public bool ActiveOnly { get; set; } = true;
}

public record ReceptionistListItemDto(
    int Id,
    string Name,
    string? LoginId,
    string? Phone,
    /// <summary>Real mailbox, for attendance codes.</summary>
    string? PersonalEmail,
    string? Branch,
    bool IsActive,

    /// <summary>Shown on the staff hierarchy chart.</summary>
    string? Designation,
    string? Qualification,
    string? ComputerTraining,

    /// <summary>Technical staff are excused qualification and computer training.</summary>
    bool IsTechnical,

    /// <summary>
    /// The current standard monthly pay — the default a new month's payment
    /// starts from. Set on the fees master screen, not here.
    /// </summary>
    decimal MonthlyPay,

    /// <summary>How many months have been paid, and the total.</summary>
    int PaymentCount,
    decimal TotalPaid,
    DateOnly? LastPaidMonth);

public record ReceptionistSaveRequest
{
    public string? Name { get; init; }
    public string? LoginId { get; init; }
    public string? Phone { get; init; }

    /// <summary>Real mailbox, for attendance confirmation codes.</summary>
    public string? PersonalEmail { get; init; }
    public string? Branch { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>The position, free text. Also what puts them on the chart.</summary>
    public string? Designation { get; init; }

    /// <summary>Required unless <see cref="IsTechnical"/>.</summary>
    public string? Qualification { get; init; }

    /// <summary>Required unless <see cref="IsTechnical"/>.</summary>
    public string? ComputerTraining { get; init; }

    /// <summary>Technical staff are excused qualification and computer training.</summary>
    public bool IsTechnical { get; init; }
}

public class ReceptionistSaveRequestValidator : AbstractValidator<ReceptionistSaveRequest>
{
    public ReceptionistSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Receptionist name is required.")
            .MaximumLength(100);

        // Required, because the whole point of the master is branch-wise reporting
        // and a receptionist with no branch would never appear in it.
        RuleFor(x => x.Branch)
            .NotEmpty().WithMessage("Choose the branch this receptionist works at.")
            .MaximumLength(100);

        RuleFor(x => x.LoginId)
            .MaximumLength(100)
            .EmailAddress().WithMessage("The user id is the email they sign in with.")
            .When(x => !string.IsNullOrWhiteSpace(x.LoginId));

        RuleFor(x => x.Phone)
            .Matches("^[0-9]{10}$").WithMessage("Enter a valid 10-digit phone number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        /* Not required here -- set from Staff Hierarchy instead, the same change made
           to the teacher master. */
        RuleFor(x => x.Qualification).MaximumLength(200);
        RuleFor(x => x.ComputerTraining).MaximumLength(200);
    }
}

public record ReceptionistMonthlyPayRequest
{
    public decimal MonthlyPay { get; init; }
}

public class ReceptionistMonthlyPayRequestValidator : AbstractValidator<ReceptionistMonthlyPayRequest>
{
    public ReceptionistMonthlyPayRequestValidator()
    {
        RuleFor(x => x.MonthlyPay)
            .GreaterThanOrEqualTo(0m).WithMessage("Monthly pay cannot be negative.")
            .LessThan(1_000_000m).WithMessage("Monthly pay must be less than 10,00,000.");
    }
}

/* ================================================ receptionist payments */

public record ReceptionistPaymentRequest
{
    public int ReceptionistId { get; init; }

    /// <summary>Any date inside the month being paid for; it is normalised to the 1st.</summary>
    public DateOnly PayMonth { get; init; }

    public DateOnly? PaidOn { get; init; }

    /// <summary>The standard monthly pay — changes only when the Admin deliberately changes the payment structure.</summary>
    public decimal Amount { get; init; }

    public string? Notes { get; init; }
}

public class ReceptionistPaymentRequestValidator : AbstractValidator<ReceptionistPaymentRequest>
{
    public ReceptionistPaymentRequestValidator()
    {
        RuleFor(x => x.ReceptionistId).GreaterThan(0).WithMessage("Choose a receptionist.");

        RuleFor(x => x.PayMonth)
            .NotEmpty().WithMessage("Choose the month this payment is for.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Amount must be greater than zero.")
            .LessThan(1_000_000m).WithMessage("Amount must be less than 10,00,000.");

        RuleFor(x => x.PaidOn)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddDays(1)))
            .WithMessage("The payment date cannot be in the future.")
            .When(x => x.PaidOn.HasValue);

        RuleFor(x => x.Notes).MaximumLength(200);
    }
}

public record ReceptionistPaymentDto(
    int Id,
    int ReceptionistId,
    string? ReceptionistName,
    DateOnly PayMonth,
    DateOnly? PaidOn,
    decimal Amount,
    decimal ExtraShiftBonus,
    string? Branch,
    string? Notes,
    string? RecordedBy,
    DateTime RecordedOn);

/* ================================================= the branch-wise report */

/// <summary>One receptionist's line in one period of the report.</summary>
public record ReceptionistReportRowDto(
    string Branch,
    DateOnly PeriodStart,
    string PeriodLabel,
    int ReceptionistId,
    string ReceptionistName,
    decimal Amount);

public record ReceptionistBranchTotalDto(
    string Branch,
    int Receptionists,
    int MonthsPaid,
    decimal TotalPaid);

/// <summary>
/// What each branch paid its receptionists, month by month.
/// </summary>
public record ReceptionistReportDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ReceptionistReportRowDto> Rows,
    IReadOnlyList<ReceptionistBranchTotalDto> BranchTotals,
    decimal GrandTotal,
    IReadOnlyList<string> Branches,
    DateTime GeneratedOn);

public class ReceptionistReportQuery
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? Branch { get; set; }
}

/* ============================================================ extra-shift bonus */

/// <summary>
/// One day this receptionist covered a colleague's absence, or was themselves
/// covered — valued at the actual hours that day's shift(s) ran, from
/// <c>AttendanceShift.StartTime</c>/<c>EndTime</c>, not a flat one-shift count.
/// </summary>
public record ExtraShiftDayDto(
    DateOnly Date,
    string OtherReceptionistName,
    string? Branch,
    decimal Hours,
    decimal Amount);

/// <summary>
/// The net extra-shift adjustment for one receptionist's upcoming payment — a
/// transfer, not money created from nothing: a day they covered for an absent
/// colleague adds that day's actual rostered hours at their <b>own</b> hourly
/// rate; a day a colleague covered <b>for them</b> deducts their own hours for
/// that day at the same rate, since they did not work it. The hourly rate
/// comes from the receptionist's standard monthly pay — the fees master
/// figure, not a recorded payment, which can lag behind or not exist yet —
/// divided by the previous pay cycle's actual rostered hours (the week-long Staff Rota
/// pattern replayed across every calendar day of that cycle), not a flat
/// shift count — see <see cref="IReceptionistService.GetExtraShiftBonusAsync"/>.
/// Computed by the server alone — never entered, never edited.
/// </summary>
public record ExtraShiftBonusDto(
    int ReceptionistId,
    decimal HourlyRate,
    decimal LastCycleHours,
    // The Receptionist.MonthlyPay the hourly rate above was computed from.
    decimal StandardPay,
    int CoveredForOthersCount,
    int CoveredByOthersCount,
    decimal NetBonus,
    IReadOnlyList<ExtraShiftDayDto> CoveredForOthers,
    IReadOnlyList<ExtraShiftDayDto> CoveredByOthers);

/* ==================================================================== service */

public interface IReceptionistService
{
    Task<PagedResult<ReceptionistListItemDto>> GetListAsync(
        ReceptionistQuery query, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        ReceptionistSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(
        int id, ReceptionistSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Removes a receptionist. Refused once payments exist against them.</summary>
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Sets the current standard monthly pay on the fees master screen — the
    /// default a new month's payment starts from, not a payment itself.
    /// </summary>
    Task<Result<bool>> UpdateMonthlyPayAsync(
        int id, decimal monthlyPay, string? actor, CancellationToken ct = default);

    /// <summary>Records or corrects one month's payment.</summary>
    Task<Result<int>> RecordPaymentAsync(
        ReceptionistPaymentRequest request, string? actor, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ReceptionistPaymentDto>>> GetPaymentsAsync(
        int receptionistId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default);

    /// <summary>Receptionist pay by branch and month.</summary>
    Task<Result<ReceptionistReportDto>> GetReportAsync(
        ReceptionistReportQuery query, CancellationToken ct = default);

    /// <summary>
    /// Leave taken against entitlement for every active receptionist in one
    /// period, plus whatever deduction decision an Admin has already recorded
    /// for that period.
    /// </summary>
    Task<Result<ReceptionistLeaveReportDto>> GetLeaveReportAsync(
        ReceptionistLeaveReportQuery query, CancellationToken ct = default);

    /// <summary>Records (or updates) the Admin's deduction decision for one receptionist's period.</summary>
    Task<Result<bool>> SetLeaveDeductionAsync(
        ReceptionistLeaveDeductionRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Suggests an extra-shift bonus for one receptionist's next payment, from the
    /// days in <paramref name="forMonth"/> where they scanned in at their branch on
    /// a day rostered to a colleague who did not — covering an absence.
    /// </summary>
    Task<Result<ExtraShiftBonusDto>> GetExtraShiftBonusAsync(
        int receptionistId, DateOnly forMonth, CancellationToken ct = default);
}

/* ==================================================== leave & deduction */

/// <summary>
/// The paid-leave entitlement mapping the owner asked for, keyed by how many
/// shifts/week the receptionist is rostered for — see <see cref="ReceptionistLeavePolicy.MonthlyEntitlementDays"/>.
/// </summary>
public static class ReceptionistLeavePolicy
{
    /// <summary>
    /// Days of paid leave per month, by weekly shift count. Banded rather than
    /// exact: a shift count that falls between two named tiers takes the lower
    /// one, so 5 shifts (above the 4 tier, below the 6) earns the 4-tier's one
    /// day rather than nothing. Below the lowest tier there is no entitlement.
    /// </summary>
    public static decimal MonthlyEntitlementDays(int shiftsPerWeek) =>
        shiftsPerWeek >= 6 ? 1.5m :
        shiftsPerWeek >= 4 ? 1m :
        shiftsPerWeek >= 3 ? 0.8m :
        shiftsPerWeek >= 2 ? 0.5m :
        0m;
}

public record ReceptionistLeaveReportLineDto(
    int ReceptionistId,
    string ReceptionistName,
    string? Branch,
    int ShiftsPerWeek,
    decimal EntitlementDays,
    decimal TakenDays,
    bool ExceedsEntitlement,
    decimal DeductionPercent,
    bool Applied,
    string? Notes);

public record ReceptionistLeaveReportDto(
    string PeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<ReceptionistLeaveReportLineDto> Rows,
    DateTime GeneratedOn);

public class ReceptionistLeaveReportQuery
{
    /// <summary><c>Weekly</c>, <c>Monthly</c>, <c>Yearly</c> or <c>Custom</c>.</summary>
    public string PeriodType { get; set; } = "Monthly";

    /// <summary>Any date inside the period; normalised to the start of the week/month/year. Ignored for Custom.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Custom range start (inclusive). Only read when <see cref="PeriodType"/> is Custom.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Custom range end (inclusive). Only read when <see cref="PeriodType"/> is Custom.</summary>
    public DateOnly? To { get; set; }

    public string? Branch { get; set; }
}

public record ReceptionistLeaveDeductionRequest
{
    public int ReceptionistId { get; init; }
    public string PeriodType { get; init; } = "Monthly";
    public DateOnly PeriodStart { get; init; }
    public decimal DeductionPercent { get; init; }
    public bool Applied { get; init; }
    public string? Notes { get; init; }
}

public class ReceptionistLeaveDeductionRequestValidator : AbstractValidator<ReceptionistLeaveDeductionRequest>
{
    public ReceptionistLeaveDeductionRequestValidator()
    {
        RuleFor(x => x.ReceptionistId).GreaterThan(0).WithMessage("Choose a receptionist.");

        RuleFor(x => x.PeriodType)
            .Must(p => p is "Weekly" or "Monthly" or "Yearly")
            .WithMessage("Period must be Weekly, Monthly or Yearly.");

        RuleFor(x => x.DeductionPercent)
            .InclusiveBetween(0m, 100m).WithMessage("Deduction percentage must be between 0 and 100.");

        RuleFor(x => x.Notes).MaximumLength(300);
    }
}
