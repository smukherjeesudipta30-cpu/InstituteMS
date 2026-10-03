using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Attendance;

/* ------------------------------------------------------------- the QR code */

/// <summary>
/// The code shown on the screen at the centre. It carries a short-lived signed
/// token, so a photograph of the QR is useless within a minute or two.
/// </summary>
public record AttendanceQrDto(
    int BranchId,
    string? BranchName,

    /// <summary>The full URL encoded into the QR. Nothing else is needed to scan it.</summary>
    string ScanUrl,

    /// <summary>When this code stops working, so the screen knows when to refresh.</summary>
    DateTime ExpiresOn,

    int SecondsValid);

/* --------------------------------------------------------------- scanning */

/// <summary>What the phone sends after reading the QR and asking for its location.</summary>
public record AttendanceScanRequest
{
    /// <summary>The signed token lifted out of the scanned URL.</summary>
    public string Token { get; init; } = string.Empty;

    public double Latitude { get; init; }
    public double Longitude { get; init; }

    /// <summary>The phone's own estimate of its accuracy, in metres.</summary>
    public double? AccuracyMetres { get; init; }
}

public class AttendanceScanRequestValidator : AbstractValidator<AttendanceScanRequest>
{
    public AttendanceScanRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("This scan is missing its code. Scan the QR again.");

        // A phone that cannot fix its position sends 0,0 - the Atlantic. Rejecting
        // it here gives a clearer message than a distance of 8,000 km would.
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Your location could not be read. Try again.")
            .NotEqual(0).WithMessage("Your location could not be read. Try again.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Your location could not be read. Try again.")
            .NotEqual(0).WithMessage("Your location could not be read. Try again.");
    }
}

/// <summary>
/// Marking attendance from one's own phone, with no code to scan.
/// </summary>
/// <remarks>
/// A phone cannot photograph a QR shown on its own screen, so a member of staff who
/// opens the site on their mobile has nothing to point a camera at. This is the same
/// act as a scan without the round trip through a code: the location still decides
/// it, and the server still decides whether it is an arrival or a departure.
/// </remarks>
public record AttendanceMarkRequest
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? AccuracyMetres { get; init; }
}

public class AttendanceMarkRequestValidator : AbstractValidator<AttendanceMarkRequest>
{
    public AttendanceMarkRequestValidator()
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Your location could not be read. Try again.")
            .NotEqual(0).WithMessage("Your location could not be read. Try again.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Your location could not be read. Try again.")
            .NotEqual(0).WithMessage("Your location could not be read. Try again.");
    }
}

/// <summary>
/// What comes back when a mark is accepted but still needs its emailed code.
/// </summary>
public record AttendanceChallengeDto(
    int ChallengeId,

    /// <summary>Where the code went, partly hidden — enough to recognise, not to read out.</summary>
    string SentTo,

    DateTime MarkedOn,

    /// <summary>Which way this mark will count once confirmed.</summary>
    string Direction,

    string? Branch,
    DateTime ExpiresOn,
    string Message);

/// <summary>The six digits typed back.</summary>
public record AttendanceConfirmRequest
{
    public int ChallengeId { get; init; }
    public string Code { get; init; } = string.Empty;
}

public class AttendanceConfirmRequestValidator : AbstractValidator<AttendanceConfirmRequest>
{
    public AttendanceConfirmRequestValidator()
    {
        RuleFor(x => x.ChallengeId).GreaterThan(0);

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Enter the six-digit code from your email.")
            .Matches("^[0-9]{6}$").WithMessage("The code is six digits.");
    }
}

/// <summary>The answer the person gets on their phone, in the words they need.</summary>
public record AttendanceScanResultDto(
    string StaffName,
    string StaffType,
    string? Branch,

    /// <summary><c>In</c> or <c>Out</c> — which one this scan turned out to be.</summary>
    string Direction,

    DateTime ScannedOn,

    /// <summary>Metres from the centre, so a refused scan can be understood.</summary>
    decimal DistanceMetres,

    /// <summary>Hours so far today once they scan out; null on an arrival.</summary>
    decimal? HoursToday,

    string Message);

/* ------------------------------------------------ the staff member's own day */

public record AttendanceScanRowDto(string Direction, DateTime ScannedOn, decimal? DistanceMetres);

/// <summary>Today at a glance, for the person themselves.</summary>
public record MyAttendanceDto(
    string StaffName,
    string StaffType,
    DateOnly OnDate,
    DateTime? FirstIn,
    DateTime? LastOut,

    /// <summary>True while they are scanned in and have not scanned out.</summary>
    bool IsCurrentlyIn,

    /// <summary>Complete in/out pairs only. Null when nothing has been paired yet.</summary>
    decimal? HoursToday,

    /// <summary>
    /// Minutes left to scan out before the day is written off as leave. Null when
    /// they are not currently scanned in. Shown as a countdown, because a warning
    /// after the deadline is of no use to anybody.
    /// </summary>
    int? MinutesLeftToScanOut,

    /// <summary>True once the cut-off has passed and this day counts as leave.</summary>
    bool CountedAsLeave,

    IReadOnlyList<AttendanceScanRowDto> Scans);

/* -------------------------------------------------------------- the report */

public enum AttendanceGrouping { Day = 0, Month = 1, Year = 2 }

public class AttendanceReportQuery
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>Day, Month or Year. Day lists each date; the others roll up.</summary>
    public AttendanceGrouping GroupBy { get; set; } = AttendanceGrouping.Day;

    /// <summary>Optional filters. Null means everyone, every branch.</summary>
    public string? StaffType { get; set; }
    public int? StaffId { get; set; }
    public string? Branch { get; set; }
}

/// <summary>One person in one period.</summary>
public record AttendanceReportRowDto(
    string StaffType,
    int StaffId,
    string? StaffName,
    string PeriodLabel,
    DateOnly PeriodStart,

    /// <summary>Days with at least one scan.</summary>
    int DaysPresent,

    /// <summary>Days in the period with no scan at all — leave, by the owner's rule.</summary>
    int DaysAbsent,

    decimal TotalHours,

    /// <summary>Average over the days actually present, not over the whole period.</summary>
    decimal AverageHoursPerPresentDay,

    /// <summary>
    /// Days lost because the person scanned in but never scanned out. There is no
    /// departure time, so there are no hours to count and the day is written off as
    /// leave. Reported on its own because it is the one kind of absence that has a
    /// remedy — the person was there, and next time can scan out.
    /// </summary>
    int DaysMissingScanOut);

/// <summary>One row per person per day, shown under a Day grouping.</summary>
public record AttendanceDayRowDto(
    DateOnly OnDate,
    string StaffType,
    int StaffId,
    string? StaffName,
    string? Branch,
    DateTime? FirstIn,
    DateTime? LastOut,
    decimal? Hours,

    /// <summary>Scanned in, never scanned out.</summary>
    bool MissingScanOut,

    /// <summary>
    /// True when the missing departure has gone past the cut-off, so this day counts
    /// as leave. False while the person is still within it — they are at work, not
    /// absent, and the day is simply not finished.
    /// </summary>
    bool CountedAsLeave,

    /// <summary>True while the shift is still open and can still be closed by scanning out.</summary>
    bool StillOpen,

    /// <summary>
    /// Every separate In/Out visit that day, in order. A day with one visit has one
    /// entry here and FirstIn/LastOut already say the whole story; a day with more
    /// than one is why FirstIn to LastOut can span far more time than Hours accounts
    /// for -- the person left and came back, rather than staying the whole span.
    /// </summary>
    IReadOnlyList<AttendanceVisitDto> Visits,

    /// <summary>
    /// A teacher's finished day under the minimum hours at the centre (3 by
    /// default), counted as absent. <see cref="CountedAsLeave"/> is true too.
    /// </summary>
    bool ShortHours = false);

/// <summary>One arrival and, if it happened yet, the departure that closed it.</summary>
public record AttendanceVisitDto(DateTime In, DateTime? Out, decimal? Hours);

public record AttendanceReportDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    IReadOnlyList<AttendanceReportRowDto> Rows,
    IReadOnlyList<AttendanceDayRowDto> Days,
    int StaffCount,
    decimal TotalHours,
    DateTime GeneratedOn);

/* ------------------------------------------------------------------ rota */

public record AttendanceShiftDto(
    int Id, string Name, TimeOnly StartTime, TimeOnly EndTime, TimeOnly ScanOutBy, bool IsActive);

/// <summary>One line of the rota as the admin screen shows it.</summary>
public record RotaLineDto(byte DayOfWeek, string DayName, int ShiftId, string ShiftName);

/// <summary>One member of staff and every day they are rostered.</summary>
public record StaffRotaDto(
    string StaffType,
    int StaffId,
    string StaffName,
    string? Branch,

    /// <summary>Whether a sign-in exists for them. False means they cannot scan at all.</summary>
    bool CanSignIn,

    IReadOnlyList<RotaLineDto> Lines);

/// <summary>
/// Replaces one person's whole rota. Sending the complete set rather than adding
/// and removing lines one at a time means the screen and the database cannot drift
/// apart halfway through an edit.
/// </summary>
public record SaveRotaRequest
{
    public string StaffType { get; init; } = "Receptionist";
    public int StaffId { get; init; }
    public IReadOnlyList<RotaLineInput> Lines { get; init; } = [];
}

public record RotaLineInput(byte DayOfWeek, int ShiftId);

public class SaveRotaRequestValidator : AbstractValidator<SaveRotaRequest>
{
    public SaveRotaRequestValidator()
    {
        RuleFor(x => x.StaffType)
            .Must(t => t is "Teacher" or "Receptionist")
            .WithMessage("Staff type must be Teacher or Receptionist.");

        RuleFor(x => x.StaffId).GreaterThan(0).WithMessage("Choose who this rota is for.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.DayOfWeek)
                .InclusiveBetween((byte)0, (byte)6).WithMessage("Day must be Sunday to Saturday.");

            line.RuleFor(l => l.ShiftId).GreaterThan(0).WithMessage("Choose a shift.");
        });
    }
}

/* ---------------------------------------------------------------- contract */

public interface IAttendanceService
{
    Task<Result<AttendanceQrDto>> GetQrAsync(int? branchId, CancellationToken ct = default);

    /* Both routes now end in a code sent by email, so both return a challenge
       rather than a finished scan. The mark is only real once ConfirmAsync
       accepts the six digits. */

    Task<Result<AttendanceChallengeDto>> ScanAsync(
        string email, AttendanceScanRequest request, CancellationToken ct = default);

    Task<Result<AttendanceChallengeDto>> MarkAsync(
        string email, AttendanceMarkRequest request, CancellationToken ct = default);

    /// <summary>Completes a mark by accepting the code that was emailed.</summary>
    Task<Result<AttendanceScanResultDto>> ConfirmAsync(
        string email, AttendanceConfirmRequest request, CancellationToken ct = default);

    /// <summary>Sends the same code again, for an email that did not arrive.</summary>
    Task<Result<AttendanceChallengeDto>> ResendAsync(
        string email, int challengeId, CancellationToken ct = default);

    Task<Result<MyAttendanceDto>> GetMyDayAsync(
        string email, DateOnly? onDate, CancellationToken ct = default);

    Task<Result<AttendanceReportDto>> GetReportAsync(
        AttendanceReportQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<AttendanceShiftDto>> GetShiftsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StaffRotaDto>> GetRotaAsync(
        string? staffType, CancellationToken ct = default);

    Task<Result<bool>> SaveRotaAsync(
        SaveRotaRequest request, string? actor, CancellationToken ct = default);
}
