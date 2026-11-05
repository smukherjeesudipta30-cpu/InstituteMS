using System.Globalization;
using InstituteMS.Application.Attendance;
using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Attendance for the PreSchool's receptionist and teachers — the Computer
/// Centre's way of taking it (see <see cref="AttendanceService"/>), on the
/// PreSchool's own tables and its own location fence.
/// </summary>
/// <remarks>
/// <para>A staff member is matched to their sign-in by the email on their
/// PreSchool staff record, as their payslips are. The code goes to that same
/// address. The housekeeper has no sign-in and is kept on paper.</para>
///
/// <para>The QR reuses the Computer Centre's signed, short-lived token with a
/// branch number no real branch can have, so a PreSchool code is refused at the
/// Computer Centre and a Computer Centre code is refused here.</para>
/// </remarks>
public class PreSchoolAttendanceService : IPreSchoolAttendanceService
{
    /// <summary>Stands in for a branch id inside the QR token. Never a real branch.</summary>
    public const int QrBranchId = -1000;

    private const int CodeValidMinutes = 15;
    private const int MaxCodeAttempts = 5;

    private static readonly string[] MarkingRoles = [PreSchoolStaffRoles.Receptionist, PreSchoolStaffRoles.Teacher];

    private readonly AppDbContext _db;
    private readonly AttendanceTokenService _tokens;
    private readonly AttendanceOptions _options;
    private readonly IStudentMailer _mailer;
    private readonly IPasswordHasher _hasher;
    private readonly string _siteBaseUrl;
    private readonly ILogger<PreSchoolAttendanceService> _logger;

    public PreSchoolAttendanceService(
        AppDbContext db,
        AttendanceTokenService tokens,
        IOptions<AttendanceOptions> options,
        IOptions<MailOptions> mail,
        IStudentMailer mailer,
        IPasswordHasher hasher,
        ILogger<PreSchoolAttendanceService> logger)
    {
        _db = db;
        _tokens = tokens;
        _options = options.Value;
        _mailer = mailer;
        _hasher = hasher;
        _siteBaseUrl = mail.Value.SiteBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    /* ============================================================ the code */

    public async Task<Result<AttendanceQrDto>> GetQrAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s?.Latitude is null || s.Longitude is null)
            return Result<AttendanceQrDto>.Failure(NoFenceMessage);

        var (token, expiresOn) = _tokens.Create(QrBranchId);
        return Result<AttendanceQrDto>.Success(new AttendanceQrDto(
            QrBranchId, s.SchoolName, $"{_siteBaseUrl}/preschool/attendance/scan?t={token}", expiresOn, _tokens.TokenSeconds));
    }

    /* ================================================== scanning or marking */

    public async Task<Result<AttendanceChallengeDto>> ScanAsync(string email, AttendanceScanRequest request, CancellationToken ct = default)
    {
        if (_tokens.ReadBranchId(request.Token) != QrBranchId)
            return Result<AttendanceChallengeDto>.Failure(
                "That code has expired, or is not the PreSchool's. Scan the code on the PreSchool screen again.");

        return await MarkAsync(email, new AttendanceMarkRequest
        {
            Latitude = request.Latitude, Longitude = request.Longitude, AccuracyMetres = request.AccuracyMetres
        }, ct);
    }

    public async Task<Result<AttendanceChallengeDto>> MarkAsync(string email, AttendanceMarkRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);
        if (staff is null) return Result<AttendanceChallengeDto>.Failure(NotLinkedMessage, ResultStatus.Forbidden);

        var s = await SettingsAsync(ct);
        if (s?.Latitude is null || s.Longitude is null) return Result<AttendanceChallengeDto>.Failure(NoFenceMessage);

        if (request.AccuracyMetres is > 0 && request.AccuracyMetres > _options.MaxAccuracyMetres)
            return Result<AttendanceChallengeDto>.Failure(
                $"Your phone could only place you to within {request.AccuracyMetres:0} metres. Step outside or turn on GPS, then try again.");

        var distance = (decimal)DistanceMetres((double)s.Latitude.Value, (double)s.Longitude.Value, request.Latitude, request.Longitude);
        if (distance > s.GeofenceMetres)
            return Result<AttendanceChallengeDto>.Failure(
                $"You have to be inside the {s.SchoolName} premises — only then can you mark your attendance. " +
                $"You are about {distance:0} metres away.", ResultStatus.Forbidden);

        if (string.IsNullOrWhiteSpace(staff.Email))
            return Result<AttendanceChallengeDto>.Failure(
                "There is no email on your staff record, so the confirmation code cannot be sent. Ask the PreSchool admin to add it.",
                ResultStatus.Forbidden);

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var last = await _db.PreSchoolAttendance.AsNoTracking()
            .Where(a => a.StaffId == staff.Id && a.OnDate == today)
            .OrderByDescending(a => a.ScannedOn)
            .FirstOrDefaultAsync(ct);

        // Two taps seconds apart are one person pressing twice, not arriving and leaving.
        if (last is not null && (now - last.ScannedOn).TotalSeconds < _options.DebounceSeconds)
            return Result<AttendanceChallengeDto>.Failure(last.Direction == "In"
                ? "You are already marked in. Mark again when you leave."
                : "Your departure is already recorded.");

        // One pending mark at a time: pressing again replaces it.
        var pending = await _db.PreSchoolAttendanceChallenges.Where(c => c.StaffId == staff.Id && c.ConsumedOn == null).ToListAsync(ct);
        _db.PreSchoolAttendanceChallenges.RemoveRange(pending);

        // A long gap since an unclosed arrival starts a fresh one, as at the Computer Centre.
        var gapped = last is not null && (now - last.ScannedOn).TotalHours >= _options.SessionGapHours;
        var direction = last is null || gapped ? "In" : last.Direction == "In" ? "Out" : "In";

        var code = NewCode();
        var challenge = new PreSchoolAttendanceChallenge
        {
            StaffId = staff.Id, StaffName = staff.Name, StaffRole = staff.Role, LoginId = email,
            MarkedOn = now, OnDate = today,
            Latitude = (decimal)request.Latitude, Longitude = (decimal)request.Longitude,
            AccuracyMetres = request.AccuracyMetres is null ? null : (decimal)request.AccuracyMetres.Value,
            DistanceMetres = decimal.Round(distance, 2),
            CodeHash = _hasher.Hash(code), SentToEmail = staff.Email,
            ExpiresOn = now.AddMinutes(CodeValidMinutes), CreatedOn = now
        };
        _db.PreSchoolAttendanceChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        var sent = await SendCodeAsync(staff, code, direction, now, s, email, ct);
        _logger.LogInformation("PreSchool attendance code issued for staff {StaffId}; email sent: {Sent}", staff.Id, sent);

        var masked = MaskEmail(staff.Email);
        return Result<AttendanceChallengeDto>.Success(new AttendanceChallengeDto(
            challenge.Id, masked, now, direction, s.SchoolName, challenge.ExpiresOn,
            sent
                ? $"A six-digit code has been sent to {masked}. Enter it to confirm your {(direction == "In" ? "arrival" : "departure")} at {now:h:mm tt}."
                : "Your mark is held, but the code email could not be sent. Try 'Send again', or ask the PreSchool admin to check your email address."));
    }

    /* ==================================================== confirming a mark */

    public async Task<Result<AttendanceScanResultDto>> ConfirmAsync(string email, AttendanceConfirmRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);
        if (staff is null) return Result<AttendanceScanResultDto>.Failure(NotLinkedMessage, ResultStatus.Forbidden);

        var challenge = await _db.PreSchoolAttendanceChallenges.FirstOrDefaultAsync(c => c.Id == request.ChallengeId, ct);
        if (challenge is null) return Result<AttendanceScanResultDto>.Failure("That code request no longer exists.", ResultStatus.NotFound);
        if (challenge.StaffId != staff.Id) return Result<AttendanceScanResultDto>.Failure("That code belongs to someone else.", ResultStatus.Forbidden);
        if (challenge.ConsumedOn is not null) return Result<AttendanceScanResultDto>.Failure("That code has already been used.", ResultStatus.Conflict);
        if (DateTime.Now > challenge.ExpiresOn)
            return Result<AttendanceScanResultDto>.Failure("That code has expired. Mark your attendance again to get a new one.");
        if (challenge.Attempts >= MaxCodeAttempts)
            return Result<AttendanceScanResultDto>.Failure("Too many wrong codes. Mark your attendance again to get a new one.", ResultStatus.Forbidden);

        if (!_hasher.Verify(request.Code.Trim(), challenge.CodeHash))
        {
            challenge.Attempts++;
            await _db.SaveChangesAsync(ct);
            var left = Math.Max(0, MaxCodeAttempts - challenge.Attempts);
            return Result<AttendanceScanResultDto>.Failure(left > 0
                ? $"That code is not right. {left} attempt(s) left."
                : "That code is not right, and there are no attempts left. Mark your attendance again.");
        }

        var todays = await _db.PreSchoolAttendance
            .Where(a => a.StaffId == staff.Id && a.OnDate == challenge.OnDate)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);
        var last = todays.LastOrDefault();
        var gapped = last is not null && (challenge.MarkedOn - last.ScannedOn).TotalHours >= _options.SessionGapHours;
        var direction = last is null || gapped ? "In" : last.Direction == "In" ? "Out" : "In";

        var row = new PreSchoolAttendance
        {
            StaffId = challenge.StaffId, StaffName = challenge.StaffName, StaffRole = challenge.StaffRole, LoginId = challenge.LoginId,
            ScannedOn = challenge.MarkedOn, OnDate = challenge.OnDate, Direction = direction,
            Latitude = challenge.Latitude, Longitude = challenge.Longitude,
            AccuracyMetres = challenge.AccuracyMetres, DistanceMetres = challenge.DistanceMetres, CreatedOn = DateTime.Now
        };
        _db.PreSchoolAttendance.Add(row);
        challenge.ConsumedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        todays.Add(row);
        var hours = direction == "Out" ? HoursFrom(todays) : null;
        var school = (await SettingsAsync(ct))?.SchoolName ?? "Avijan PreSchool";

        var message = direction == "In"
            ? $"Welcome, {staff.Name}. You are marked in at {challenge.MarkedOn:h:mm tt}."
            : $"Goodbye, {staff.Name}. Marked out at {challenge.MarkedOn:h:mm tt}" + (hours is null ? "." : $" — {hours:0.00} hour(s) today.");

        return Result<AttendanceScanResultDto>.Success(new AttendanceScanResultDto(
            staff.Name, staff.Role, school, direction, challenge.MarkedOn, challenge.DistanceMetres ?? 0m, hours, message));
    }

    public async Task<Result<AttendanceChallengeDto>> ResendAsync(string email, int challengeId, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);
        if (staff is null) return Result<AttendanceChallengeDto>.Failure(NotLinkedMessage, ResultStatus.Forbidden);

        var challenge = await _db.PreSchoolAttendanceChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct);
        if (challenge is null || challenge.StaffId != staff.Id)
            return Result<AttendanceChallengeDto>.Failure("That code request no longer exists.", ResultStatus.NotFound);
        if (challenge.ConsumedOn is not null)
            return Result<AttendanceChallengeDto>.Failure("That mark is already confirmed.", ResultStatus.Conflict);

        // A fresh code, so an earlier email cannot be used; the mark keeps its time.
        var code = NewCode();
        challenge.CodeHash = _hasher.Hash(code);
        challenge.ExpiresOn = DateTime.Now.AddMinutes(CodeValidMinutes);
        challenge.Attempts = 0;
        await _db.SaveChangesAsync(ct);

        var s = await SettingsAsync(ct);
        var direction = await NextDirectionAsync(staff.Id, challenge.OnDate, challenge.MarkedOn, ct);
        var sent = await SendCodeAsync(staff with { Email = challenge.SentToEmail }, code, direction, challenge.MarkedOn, s, email, ct);
        var masked = MaskEmail(challenge.SentToEmail);

        return Result<AttendanceChallengeDto>.Success(new AttendanceChallengeDto(
            challenge.Id, masked, challenge.MarkedOn, direction, s?.SchoolName, challenge.ExpiresOn,
            sent ? $"A new code has been sent to {masked}." : "The code could not be sent. Ask the PreSchool admin to check your email address."));
    }

    /* ================================================== the person's own day */

    public async Task<Result<MyAttendanceDto>> GetMyDayAsync(string email, DateOnly? onDate, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);
        if (staff is null) return Result<MyAttendanceDto>.Failure(NotLinkedMessage, ResultStatus.Forbidden);

        var day = onDate ?? DateOnly.FromDateTime(DateTime.Now);
        var scans = await _db.PreSchoolAttendance.AsNoTracking()
            .Where(a => a.StaffId == staff.Id && a.OnDate == day)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);

        return Result<MyAttendanceDto>.Success(new MyAttendanceDto(
            staff.Name, staff.Role, day,
            scans.FirstOrDefault(x => x.Direction == "In")?.ScannedOn,
            scans.LastOrDefault(x => x.Direction == "Out")?.ScannedOn,
            scans.LastOrDefault()?.Direction == "In",
            HoursFrom(scans),
            null,
            false,
            scans.Select(x => new AttendanceScanRowDto(x.Direction, x.ScannedOn, x.DistanceMetres)).ToList()));
    }

    /* ============================================================= report */

    public async Task<Result<PreSchoolAttendanceReportDto>> GetReportAsync(DateOnly from, DateOnly to, int? staffId, CancellationToken ct = default)
    {
        if (to < from) return Result<PreSchoolAttendanceReportDto>.Failure("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 400) return Result<PreSchoolAttendanceReportDto>.Failure("Pick at most about a year at a time.");

        var staff = await _db.PreSchoolStaff.AsNoTracking()
            .Where(x => MarkingRoles.Contains(x.Role))
            .Where(x => x.JoinedOn <= to && (x.LeftOn == null || x.LeftOn >= from))
            .Where(x => staffId == null || x.Id == staffId)
            .OrderBy(x => x.Role).ThenBy(x => x.Name)
            .ToListAsync(ct);
        var ids = staff.Select(x => x.Id).ToList();

        var scans = await _db.PreSchoolAttendance.AsNoTracking()
            .Where(a => ids.Contains(a.StaffId) && a.OnDate >= from && a.OnDate <= to)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);

        // Days the school is shut are not absences.
        var holidays = await _db.PreSchoolHolidays.AsNoTracking()
            .Where(h => h.ToDate >= from && h.FromDate <= to)
            .Select(h => new { h.FromDate, h.ToDate })
            .ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        bool Working(DateOnly d) => d.DayOfWeek != DayOfWeek.Sunday && !holidays.Any(h => d >= h.FromDate && d <= h.ToDate);

        var days = new List<PreSchoolAttendanceDayDto>();
        var rows = new List<PreSchoolAttendanceStaffRowDto>();

        foreach (var person in staff)
        {
            var mine = scans.Where(a => a.StaffId == person.Id).GroupBy(a => a.OnDate).OrderBy(g => g.Key).ToList();
            var missingOut = 0;
            decimal total = 0;

            foreach (var g in mine)
            {
                var list = g.ToList();
                var hours = HoursFrom(list);
                var open = list[^1].Direction == "In" && g.Key < today;
                if (open) missingOut++;
                total += hours ?? 0;
                days.Add(new PreSchoolAttendanceDayDto(
                    g.Key, person.Id, person.Name, person.Role,
                    list.FirstOrDefault(x => x.Direction == "In")?.ScannedOn,
                    list.LastOrDefault(x => x.Direction == "Out")?.ScannedOn,
                    hours, open, VisitsFrom(list)));
            }

            // Working days they were on the staff and should have come in, up to today.
            var first = new[] { from, person.JoinedOn }.Max();
            var last = new[] { to, person.LeftOn ?? to, today }.Min();
            var present = mine.Select(g => g.Key).ToHashSet();
            var absent = 0;
            for (var d = first; d <= last; d = d.AddDays(1))
                if (Working(d) && !present.Contains(d)) absent++;

            rows.Add(new PreSchoolAttendanceStaffRowDto(person.Id, person.Name, person.Role, present.Count, absent, total, missingOut));
        }

        return Result<PreSchoolAttendanceReportDto>.Success(new PreSchoolAttendanceReportDto(
            from, to, rows, days.OrderBy(d => d.OnDate).ThenBy(d => d.StaffName).ToList(), rows.Sum(r => r.TotalHours)));
    }

    /* ============================================================ helpers */

    private const string NotLinkedMessage =
        "Your sign-in is not linked to a PreSchool receptionist or teacher record, so attendance cannot be recorded. " +
        "Ask the PreSchool admin to put your sign-in email on your staff record.";

    private const string NoFenceMessage =
        "The PreSchool's location is not set yet, so attendance cannot be taken. The PreSchool admin sets it in School Settings.";

    private sealed record StaffRef(int Id, string Name, string Role, string? Email);

    /// <summary>The active receptionist or teacher whose staff record carries this sign-in email.</summary>
    private async Task<StaffRef?> ResolveStaffAsync(string email, CancellationToken ct)
    {
        var me = email.Trim();
        if (me.Length == 0) return null;
        var today = DateOnly.FromDateTime(DateTime.Now);

        return await _db.PreSchoolStaff.AsNoTracking()
            .Where(x => x.Email == me && MarkingRoles.Contains(x.Role) && (x.LeftOn == null || x.LeftOn >= today))
            .OrderBy(x => x.Id)
            .Select(x => new StaffRef(x.Id, x.Name, x.Role, x.Email))
            .FirstOrDefaultAsync(ct);
    }

    private Task<PreSchoolSetting?> SettingsAsync(CancellationToken ct) =>
        _db.PreSchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);

    private async Task<string> NextDirectionAsync(int staffId, DateOnly day, DateTime at, CancellationToken ct)
    {
        var last = await _db.PreSchoolAttendance.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.OnDate == day)
            .OrderByDescending(a => a.ScannedOn)
            .FirstOrDefaultAsync(ct);
        var gapped = last is not null && (at - last.ScannedOn).TotalHours >= _options.SessionGapHours;
        return last is null || gapped ? "In" : last.Direction == "In" ? "Out" : "In";
    }

    private Task<bool> SendCodeAsync(StaffRef staff, string code, string direction, DateTime markedOn,
        PreSchoolSetting? s, string actor, CancellationToken ct)
    {
        var school = s?.SchoolName ?? "Avijan PreSchool";
        var footer = string.Join(" | ", new[] { s?.Address, string.IsNullOrWhiteSpace(s?.Phone) ? null : $"Phone {s!.Phone}" }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return _mailer.SendAttendanceCodeAsync(
            new AttendanceCodeMailRequest(staff.Name, staff.Email ?? string.Empty, code, direction, school, markedOn, CodeValidMinutes,
                school, footer.Length > 0 ? footer : school),
            actor, ct);
    }

    private static string NewCode() =>
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "your email";
        var at = email.IndexOf('@');
        if (at <= 0) return "your email";
        var name = email[..at];
        var visible = name.Length <= 2 ? name : name[..2];
        return $"{visible}{new string('•', Math.Max(2, name.Length - visible.Length))}{email[at..]}";
    }

    /// <summary>Hours from complete In/Out pairs only; an unclosed arrival adds nothing.</summary>
    private static decimal? HoursFrom(IReadOnlyList<PreSchoolAttendance> scans)
    {
        decimal minutes = 0;
        DateTime? openedAt = null;
        foreach (var scan in scans.OrderBy(x => x.ScannedOn))
        {
            if (scan.Direction == "In") openedAt = scan.ScannedOn;
            else if (openedAt is not null)
            {
                minutes += (decimal)(scan.ScannedOn - openedAt.Value).TotalMinutes;
                openedAt = null;
            }
        }
        return minutes <= 0 ? null : decimal.Round(minutes / 60m, 2);
    }

    private static List<AttendanceVisitDto> VisitsFrom(IReadOnlyList<PreSchoolAttendance> scans)
    {
        var visits = new List<AttendanceVisitDto>();
        DateTime? openedAt = null;
        foreach (var scan in scans.OrderBy(x => x.ScannedOn))
        {
            if (scan.Direction == "In")
            {
                if (openedAt is not null) visits.Add(new AttendanceVisitDto(openedAt.Value, null, null));
                openedAt = scan.ScannedOn;
            }
            else if (openedAt is not null)
            {
                visits.Add(new AttendanceVisitDto(openedAt.Value, scan.ScannedOn,
                    decimal.Round((decimal)(scan.ScannedOn - openedAt.Value).TotalMinutes / 60m, 2)));
                openedAt = null;
            }
        }
        if (openedAt is not null) visits.Add(new AttendanceVisitDto(openedAt.Value, null, null));
        return visits;
    }

    private static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6_371_000d;
        var dLat = (lat2 - lat1) * Math.PI / 180d;
        var dLon = (lon2 - lon1) * Math.PI / 180d;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180d) * Math.Cos(lat2 * Math.PI / 180d) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
