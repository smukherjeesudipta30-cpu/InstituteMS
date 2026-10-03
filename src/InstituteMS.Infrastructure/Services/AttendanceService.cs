using System.Globalization;
using InstituteMS.Application.Attendance;
using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Notifications;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Staff attendance: the code on the centre's screen, the scan from the phone, and
/// the hours that come out of pairing the two ends of a day.
/// </summary>
public class AttendanceService : IAttendanceService
{
    /// <summary>How long an emailed code lasts. Generous: Gmail is not instant.</summary>
    private const int CodeValidMinutes = 15;

    /// <summary>
    /// Wrong guesses allowed. Six digits is a million to one per try, but unlimited
    /// tries is not, so the challenge dies after a handful.
    /// </summary>
    private const int MaxCodeAttempts = 5;

    private readonly AppDbContext _db;
    private readonly AttendanceTokenService _tokens;
    private readonly AttendanceOptions _options;
    private readonly IStudentMailer _mailer;
    private readonly IPasswordHasher _hasher;
    private readonly string _siteBaseUrl;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(
        AppDbContext db,
        AttendanceTokenService tokens,
        IOptions<AttendanceOptions> options,
        IOptions<MailOptions> mail,
        IStudentMailer mailer,
        IPasswordHasher hasher,
        ILogger<AttendanceService> logger)
    {
        _db = db;
        _tokens = tokens;
        _options = options.Value;
        _mailer = mailer;
        _hasher = hasher;
        _siteBaseUrl = mail.Value.SiteBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    /// <summary>
    /// Six digits from a cryptographic source. Random.Shared would be predictable
    /// enough to guess a colleague's code from one's own.
    /// </summary>
    private static string NewCode() =>
        System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>ri••••@gmail.com</c> — enough for the owner to recognise their own address,
    /// not enough for somebody looking over their shoulder to write it down.
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "your email";

        var at = email.IndexOf('@');
        if (at <= 0) return "your email";

        var name = email[..at];
        var visible = name.Length <= 2 ? name : name[..2];

        return $"{visible}{new string('•', Math.Max(2, name.Length - visible.Length))}{email[at..]}";
    }

    /* ============================================================ the code */

    public async Task<Result<AttendanceQrDto>> GetQrAsync(
        int? branchId, CancellationToken ct = default)
    {
        var branch = branchId is > 0
            ? await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct)
            : await _db.Branches.AsNoTracking()
                .Where(b => b.IsActive && b.Latitude != null)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(ct);

        if (branch is null)
            return Result<AttendanceQrDto>.Failure("That branch was not found.", ResultStatus.NotFound);

        /* A branch with no coordinates has no fence, and a fence is the only thing
           making this attendance rather than an honesty box. Refused outright. */
        if (branch.Latitude is null || branch.Longitude is null)
        {
            return Result<AttendanceQrDto>.Failure(
                $"{branch.Name} has no location set, so attendance cannot be taken there. " +
                "Set the branch coordinates first.");
        }

        var (token, expiresOn) = _tokens.Create(branch.Id);

        return Result<AttendanceQrDto>.Success(new AttendanceQrDto(
            branch.Id,
            branch.Name,
            $"{_siteBaseUrl}/attendance/scan?t={token}",
            expiresOn,
            _tokens.TokenSeconds));
    }

    /* ============================================================ the scan */

    public async Task<Result<AttendanceChallengeDto>> ScanAsync(
        string email, AttendanceScanRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);

        if (staff is null)
        {
            return Result<AttendanceChallengeDto>.Failure(
                "Your sign-in is not linked to a teacher or receptionist record, so " +
                "attendance cannot be recorded. Ask the office to check it.",
                ResultStatus.Forbidden);
        }

        var branchId = _tokens.ReadBranchId(request.Token);

        if (branchId is null)
        {
            /* Malformed, forged and expired all land here on purpose. The honest
               case — a code that went stale while the person walked over — is by
               far the likeliest, so that is the wording. */
            return Result<AttendanceChallengeDto>.Failure(
                "That code has expired. The screen at the centre shows a fresh one — scan it again.");
        }

        var branch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == branchId, ct);

        if (branch?.Latitude is null || branch.Longitude is null)
        {
            return Result<AttendanceChallengeDto>.Failure(
                "That centre has no location set, so attendance cannot be recorded there.");
        }

        return await RecordAsync(
            staff, email, branch, request.Latitude, request.Longitude, request.AccuracyMetres, ct);
    }

    /// <summary>
    /// Marks attendance from the staff member's own phone, with no code to scan.
    /// </summary>
    /// <remarks>
    /// The branch is worked out from where they are standing rather than from a
    /// code, so this behaves correctly the day a second centre opens: whichever
    /// branch's fence they are inside is the one they are present at. Being inside
    /// no fence at all is refused in the same words as a scan from the wrong place.
    /// </remarks>
    public async Task<Result<AttendanceChallengeDto>> MarkAsync(
        string email, AttendanceMarkRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);

        if (staff is null)
        {
            return Result<AttendanceChallengeDto>.Failure(
                "Your sign-in is not linked to a teacher or receptionist record, so " +
                "attendance cannot be recorded. Ask the office to check it.",
                ResultStatus.Forbidden);
        }

        var fenced = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive && b.Latitude != null && b.Longitude != null)
            .ToListAsync(ct);

        if (fenced.Count == 0)
        {
            return Result<AttendanceChallengeDto>.Failure(
                "No centre has its location set yet, so attendance cannot be recorded. " +
                "Ask the office to set the branch coordinates.");
        }

        // Nearest first, so a person standing between two centres is recorded at the
        // one they are actually in rather than whichever happens to be listed first.
        var nearest = fenced
            .Select(b => new
            {
                Branch = b,
                Distance = DistanceMetres(
                    (double)b.Latitude!.Value, (double)b.Longitude!.Value,
                    request.Latitude, request.Longitude)
            })
            .OrderBy(x => x.Distance)
            .First();

        return await RecordAsync(
            staff, email, nearest.Branch,
            request.Latitude, request.Longitude, request.AccuracyMetres, ct);
    }

    /* ==================================================== confirming a mark */

    /// <summary>
    /// Completes a mark by accepting the six digits that were emailed.
    /// </summary>
    /// <remarks>
    /// The attendance is written with the time the person pressed the button, not
    /// the time they typed the code. A slow email must not shorten anybody's day.
    /// </remarks>
    public async Task<Result<AttendanceScanResultDto>> ConfirmAsync(
        string email, AttendanceConfirmRequest request, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);

        if (staff is null)
        {
            return Result<AttendanceScanResultDto>.Failure(
                "Your sign-in is not linked to a staff record.", ResultStatus.Forbidden);
        }

        var challenge = await _db.AttendanceChallenges
            .FirstOrDefaultAsync(c => c.Id == request.ChallengeId, ct);

        if (challenge is null)
            return Result<AttendanceScanResultDto>.Failure("That code request no longer exists.", ResultStatus.NotFound);

        // Ownership, not just existence: the id comes from the browser.
        if (challenge.StaffType != staff.Type || challenge.StaffId != staff.Id)
            return Result<AttendanceScanResultDto>.Failure("That code belongs to someone else.", ResultStatus.Forbidden);

        if (challenge.ConsumedOn is not null)
            return Result<AttendanceScanResultDto>.Failure("That code has already been used.", ResultStatus.Conflict);

        if (DateTime.Now > challenge.ExpiresOn)
        {
            return Result<AttendanceScanResultDto>.Failure(
                "That code has expired. Mark your attendance again to get a new one.");
        }

        if (challenge.Attempts >= MaxCodeAttempts)
        {
            return Result<AttendanceScanResultDto>.Failure(
                "Too many wrong codes. Mark your attendance again to get a new one.",
                ResultStatus.Forbidden);
        }

        if (!_hasher.Verify(request.Code.Trim(), challenge.CodeHash))
        {
            // Counted before the answer goes back, so a failed guess costs a try even
            // if the caller abandons the response.
            challenge.Attempts++;
            await _db.SaveChangesAsync(ct);

            var left = Math.Max(0, MaxCodeAttempts - challenge.Attempts);

            return Result<AttendanceScanResultDto>.Failure(
                left > 0
                    ? $"That code is not right. {left} attempt(s) left."
                    : "That code is not right, and there are no attempts left. Mark your attendance again.");
        }

        /* Direction is decided now rather than when the challenge was made: if the
           person marked twice and confirmed out of order, what matters is the state
           of the day at the moment it is written. */
        var todaysScans = await _db.StaffAttendance
            .Where(a => a.StaffType == staff.Type && a.StaffId == staff.Id && a.OnDate == challenge.OnDate)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);

        var direction = todaysScans.LastOrDefault()?.Direction == "In" ? "Out" : "In";

        var scan = new StaffAttendance
        {
            StaffType = challenge.StaffType,
            StaffId = challenge.StaffId,
            LoginId = challenge.LoginId,
            StaffName = challenge.StaffName,
            BranchId = challenge.BranchId,
            Branch = challenge.Branch,
            ScannedOn = challenge.MarkedOn,
            OnDate = challenge.OnDate,
            Direction = direction,
            Latitude = challenge.Latitude,
            Longitude = challenge.Longitude,
            AccuracyMetres = challenge.AccuracyMetres,
            DistanceMetres = challenge.DistanceMetres,
            CreatedOn = DateTime.Now
        };

        _db.StaffAttendance.Add(scan);
        challenge.ConsumedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        todaysScans.Add(scan);
        var hours = direction == "Out" ? HoursFrom(todaysScans) : null;

        _logger.LogInformation(
            "Attendance {Direction} confirmed by code for {StaffType} {StaffId} at {Branch}",
            direction, staff.Type, staff.Id, challenge.Branch);

        var message = direction == "In"
            ? $"Welcome, {staff.Name}. You are marked in at {challenge.MarkedOn:h:mm tt}."
            : $"Goodbye, {staff.Name}. Marked out at {challenge.MarkedOn:h:mm tt}" +
              (hours is null ? "." : $" — {hours:0.00} hour(s) today.") +
              (staff.Type == UserRoles.Teacher && (hours ?? 0m) < _options.TeacherMinimumHours
               && (_options.TeacherMinimumHoursFrom is null || challenge.OnDate >= _options.TeacherMinimumHoursFrom)
                  ? $" That is under the {_options.TeacherMinimumHours:0.#} hours a teacher must be at the centre, " +
                    "so today will count as absent unless you come back and make it up."
                  : "");

        return Result<AttendanceScanResultDto>.Success(new AttendanceScanResultDto(
            staff.Name, staff.Type, challenge.Branch, direction,
            challenge.MarkedOn, challenge.DistanceMetres ?? 0m, hours, message));
    }

    /// <summary>Sends the same challenge's code again, for an email that never came.</summary>
    public async Task<Result<AttendanceChallengeDto>> ResendAsync(
        string email, int challengeId, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);

        if (staff is null)
            return Result<AttendanceChallengeDto>.Failure("Your sign-in is not linked to a staff record.", ResultStatus.Forbidden);

        var challenge = await _db.AttendanceChallenges
            .FirstOrDefaultAsync(c => c.Id == challengeId, ct);

        if (challenge is null || challenge.StaffType != staff.Type || challenge.StaffId != staff.Id)
            return Result<AttendanceChallengeDto>.Failure("That code request no longer exists.", ResultStatus.NotFound);

        if (challenge.ConsumedOn is not null)
            return Result<AttendanceChallengeDto>.Failure("That mark is already confirmed.", ResultStatus.Conflict);

        /* A fresh code, not the old one: the previous email may be sitting in
           somebody else's inbox, and re-sending it would keep it alive. The clock
           restarts, but the MARK time does not — that stays when they pressed. */
        var code = NewCode();
        challenge.CodeHash = _hasher.Hash(code);
        challenge.ExpiresOn = DateTime.Now.AddMinutes(CodeValidMinutes);
        challenge.Attempts = 0;

        await _db.SaveChangesAsync(ct);

        var sent = await _mailer.SendAttendanceCodeAsync(
            new AttendanceCodeMailRequest(
                staff.Name, challenge.SentToEmail ?? string.Empty, code,
                "In", challenge.Branch, challenge.MarkedOn, CodeValidMinutes),
            email, ct);

        return Result<AttendanceChallengeDto>.Success(new AttendanceChallengeDto(
            challenge.Id,
            MaskEmail(challenge.SentToEmail),
            challenge.MarkedOn,
            "In",
            challenge.Branch,
            challenge.ExpiresOn,
            sent
                ? $"A new code has been sent to {MaskEmail(challenge.SentToEmail)}."
                : "The code could not be sent. Ask the office to check your email address."));
    }

    /// <summary>
    /// The part both routes share: check the fence, then send a code and wait.
    /// </summary>
    private async Task<Result<AttendanceChallengeDto>> RecordAsync(
        StaffRef staff,
        string email,
        Domain.Entities.Branch branch,
        double latitude,
        double longitude,
        double? accuracyMetres,
        CancellationToken ct)
    {
        // A phone that admits to being vague makes the fence meaningless.
        if (accuracyMetres is > 0 && accuracyMetres > _options.MaxAccuracyMetres)
        {
            return Result<AttendanceChallengeDto>.Failure(
                $"Your phone could only place you to within {accuracyMetres:0} metres. " +
                "Step outside or turn on GPS, then try again.");
        }

        var distance = (decimal)DistanceMetres(
            (double)branch.Latitude!.Value, (double)branch.Longitude!.Value,
            latitude, longitude);

        if (distance > branch.GeofenceMetres)
        {
            /* The owner's own wording, with the distance kept on the end. Without it
               someone standing at the door and being refused has no way to tell a
               poor GPS fix from a fence that is set wrongly. */
            return Result<AttendanceChallengeDto>.Failure(
                $"You have to enter Avijan Computer Center ({branch.Name}) premises — " +
                $"only then can you mark your attendance. You are about {distance:0} metres away.",
                ResultStatus.Forbidden);
        }

        /* No address, no code, no attendance. Said plainly here rather than letting
           the person wait for an email that was never going to arrive. */
        if (string.IsNullOrWhiteSpace(staff.PersonalEmail))
        {
            return Result<AttendanceChallengeDto>.Failure(
                "There is no personal email on your staff record, so the confirmation " +
                "code cannot be sent. Ask the office to add it.",
                ResultStatus.Forbidden);
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        var todaysScans = await _db.StaffAttendance
            .AsNoTracking()
            .Where(a => a.StaffType == staff.Type && a.StaffId == staff.Id && a.OnDate == today)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);

        var last = todaysScans.LastOrDefault();

        // Two marks seconds apart are one person tapping twice, not an arrival and an
        // immediate departure.
        if (last is not null && (now - last.ScannedOn).TotalSeconds < _options.DebounceSeconds)
        {
            return Result<AttendanceChallengeDto>.Failure(
                last.Direction == "In"
                    ? "You are already marked in. Mark again when you leave."
                    : "Your departure is already recorded.");
        }

        /* An outstanding challenge is replaced rather than added to. Somebody who
           pressed twice because the first email was slow should end up with one
           pending mark and one live code, not two of each. */
        var pending = await _db.AttendanceChallenges
            .Where(c => c.StaffType == staff.Type && c.StaffId == staff.Id && c.ConsumedOn == null)
            .ToListAsync(ct);

        _db.AttendanceChallenges.RemoveRange(pending);

        /* Alternates In/Out as normal, but a long gap since the last scan starts a
           fresh arrival even if that last scan was itself an unclosed "In" — see
           AttendanceOptions.SessionGapHours. Without this, one missed scan-out
           silently flips every direction for the rest of the day. */
        var gappedSinceLast = last is not null
            && (now - last.ScannedOn).TotalHours >= _options.SessionGapHours;

        var direction = last is null || gappedSinceLast
            ? "In"
            : last.Direction == "In" ? "Out" : "In";

        var code = NewCode();

        var challenge = new AttendanceChallenge
        {
            StaffType = staff.Type,
            StaffId = staff.Id,
            StaffName = staff.Name,
            LoginId = email,
            BranchId = branch.Id,
            Branch = branch.Name,
            MarkedOn = now,
            OnDate = today,
            Latitude = (decimal)latitude,
            Longitude = (decimal)longitude,
            AccuracyMetres = accuracyMetres is null ? null : (decimal)accuracyMetres.Value,
            DistanceMetres = distance,
            CodeHash = _hasher.Hash(code),
            SentToEmail = staff.PersonalEmail,
            ExpiresOn = now.AddMinutes(CodeValidMinutes),
            CreatedOn = now
        };

        _db.AttendanceChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        var sent = await _mailer.SendAttendanceCodeAsync(
            new AttendanceCodeMailRequest(
                staff.Name, staff.PersonalEmail!, code, direction,
                branch.Name, now, CodeValidMinutes),
            email, ct);

        _logger.LogInformation(
            "Attendance code issued for {StaffType} {StaffId} at {Branch}; email sent: {Sent}",
            staff.Type, staff.Id, branch.Name, sent);

        var masked = MaskEmail(staff.PersonalEmail);

        return Result<AttendanceChallengeDto>.Success(new AttendanceChallengeDto(
            challenge.Id,
            masked,
            now,
            direction,
            branch.Name,
            challenge.ExpiresOn,
            sent
                ? $"A six-digit code has been sent to {masked}. Enter it to confirm your " +
                  $"{(direction == "In" ? "arrival" : "departure")} at {now:h:mm tt}."
                : "Your mark is held, but the code email could not be sent. Try 'Send again', " +
                  "or ask the office to check your email address."));
    }

    /* ================================================== the person's own day */

    public async Task<Result<MyAttendanceDto>> GetMyDayAsync(
        string email, DateOnly? onDate, CancellationToken ct = default)
    {
        var staff = await ResolveStaffAsync(email, ct);

        if (staff is null)
        {
            return Result<MyAttendanceDto>.Failure(
                "Your sign-in is not linked to a teacher or receptionist record.",
                ResultStatus.Forbidden);
        }

        var day = onDate ?? DateOnly.FromDateTime(DateTime.Now);

        var scans = await _db.StaffAttendance
            .AsNoTracking()
            .Where(a => a.StaffType == staff.Type && a.StaffId == staff.Id && a.OnDate == day)
            .OrderBy(a => a.ScannedOn)
            .ToListAsync(ct);

        var isIn = scans.LastOrDefault()?.Direction == "In";

        int? minutesLeft = null;
        var countedAsLeave = false;

        if (isIn)
        {
            var rota = await RotaForAsync(staff.Type, staff.Id, ct);
            var deadline = DeadlineFor(rota, day, scans[^1].ScannedOn);
            var remaining = deadline - DateTime.Now;

            countedAsLeave = remaining <= TimeSpan.Zero;
            minutesLeft = countedAsLeave ? 0 : (int)Math.Ceiling(remaining.TotalMinutes);
        }

        return Result<MyAttendanceDto>.Success(new MyAttendanceDto(
            staff.Name,
            staff.Type,
            day,
            scans.FirstOrDefault(s => s.Direction == "In")?.ScannedOn,
            scans.LastOrDefault(s => s.Direction == "Out")?.ScannedOn,
            isIn,
            HoursFrom(scans),
            minutesLeft,
            countedAsLeave,
            scans.Select(s => new AttendanceScanRowDto(s.Direction, s.ScannedOn, s.DistanceMetres)).ToList()));
    }

    /* ============================================================= report */

    public async Task<Result<AttendanceReportDto>> GetReportAsync(
        AttendanceReportQuery query, CancellationToken ct = default)
    {
        if (query.To < query.From)
            return Result<AttendanceReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var q = _db.StaffAttendance.AsNoTracking()
            .Where(a => a.OnDate >= query.From && a.OnDate <= query.To);

        if (!string.IsNullOrWhiteSpace(query.StaffType))
        {
            var type = query.StaffType.Trim();
            q = q.Where(a => a.StaffType == type);
        }

        if (query.StaffId is > 0)
            q = q.Where(a => a.StaffId == query.StaffId);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(a => a.Branch == branch);
        }

        var scans = await q.OrderBy(a => a.ScannedOn).ToListAsync(ct);

        // One entry per person per day: the shape both halves of the report need.
        var now = DateTime.Now;

        /* The whole rota, read once and grouped by person. Anyone absent from it —
           every teacher, and any receptionist covering an unrostered day — falls
           through to the flat rule inside DeadlineFor. */
        var rota = (await _db.StaffShiftAssignments.AsNoTracking()
                .Where(a => a.IsActive)
                .Join(_db.AttendanceShifts.Where(s => s.IsActive),
                      a => a.ShiftId,
                      s => s.Id,
                      (a, s) => new
                      {
                          a.StaffType,
                          a.StaffId,
                          Entry = new RotaEntry(a.DayOfWeek, s.StartTime, s.ScanOutBy)
                      })
                .ToListAsync(ct))
            .GroupBy(x => (x.StaffType, x.StaffId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RotaEntry>)g.Select(x => x.Entry).ToList());

        static IReadOnlyList<RotaEntry> Empty() => Array.Empty<RotaEntry>();

        var days = scans
            .GroupBy(a => new { a.StaffType, a.StaffId, a.OnDate })
            .Select(g =>
            {
                var ordered = g.OrderBy(a => a.ScannedOn).ToList();
                var hours = HoursFrom(ordered);
                var visits = VisitsFrom(ordered);

                var missingOut = ordered[^1].Direction == "In";

                /* An arrival with no departure is only leave once the deadline has
                   passed. Before that the person is at work and the day is simply
                   unfinished — marking them absent mid-shift would be wrong, and
                   would flicker back to present the moment they scanned out.

                   A rostered person's deadline is their shift's clock time for that
                   weekday; anyone else gets eight hours from when they arrived. */
                var theirRota = rota.TryGetValue((g.Key.StaffType, g.Key.StaffId), out var r)
                    ? r
                    : Empty();

                var deadline = DeadlineFor(theirRota, g.Key.OnDate, ordered[^1].ScannedOn);
                var stillOpen = missingOut && now < deadline;

                /* A teacher must be at the centre at least TeacherMinimumHours in
                   the day. Judged only once the day is over — before then they may
                   step out and come back — and counted as absent like a missing
                   scan-out. */
                var dayOver = g.Key.OnDate < DateOnly.FromDateTime(now) || now >= deadline;
                var shortHours = !missingOut && dayOver
                    && g.Key.StaffType == UserRoles.Teacher
                    && (_options.TeacherMinimumHoursFrom is null || g.Key.OnDate >= _options.TeacherMinimumHoursFrom)
                    && (hours ?? 0m) < _options.TeacherMinimumHours;

                var countedAsLeave = (missingOut && !stillOpen) || shortHours;

                return new AttendanceDayRowDto(
                    g.Key.OnDate,
                    g.Key.StaffType,
                    g.Key.StaffId,
                    ordered[0].StaffName,
                    ordered[0].Branch,
                    ordered.FirstOrDefault(a => a.Direction == "In")?.ScannedOn,
                    ordered.LastOrDefault(a => a.Direction == "Out")?.ScannedOn,
                    hours,
                    missingOut,
                    countedAsLeave,
                    stillOpen,
                    visits,
                    shortHours);
            })
            .OrderBy(d => d.OnDate).ThenBy(d => d.StaffName)
            .ToList();

        /* Every day in the range counts as a working day — the owner's rule, and the
           reason a day with no scan is simply leave. Sundays included. */
        var totalDaysInRange = query.To.DayNumber - query.From.DayNumber + 1;

        var rows = days
            .GroupBy(d => new
            {
                d.StaffType,
                d.StaffId,
                Period = PeriodStartOf(d.OnDate, query.GroupBy)
            })
            .Select(g =>
            {
                /* A day written off for a missing departure scan is not a day
                   present, however long the person actually stood there. That is
                   the owner's rule, and the whole point of it is that the count
                   staff see must make forgetting to scan out cost something. */
                var present = g.Count(d => !d.CountedAsLeave);
                var totalHours = g.Sum(d => d.Hours ?? 0m);

                var expected = query.GroupBy == AttendanceGrouping.Day
                    ? 1
                    : DaysInPeriod(g.Key.Period, query.GroupBy, query.From, query.To);

                return new AttendanceReportRowDto(
                    g.Key.StaffType,
                    g.Key.StaffId,
                    g.First().StaffName,
                    PeriodLabelOf(g.Key.Period, query.GroupBy),
                    g.Key.Period,
                    present,
                    Math.Max(0, expected - present),
                    decimal.Round(totalHours, 2),
                    present == 0 ? 0m : decimal.Round(totalHours / present, 2),
                    g.Count(d => d.CountedAsLeave));
            })
            .OrderBy(r => r.PeriodStart).ThenBy(r => r.StaffName)
            .ToList();

        return Result<AttendanceReportDto>.Success(new AttendanceReportDto(
            query.From,
            query.To,
            query.GroupBy.ToString(),
            rows,
            days,
            days.Select(d => (d.StaffType, d.StaffId)).Distinct().Count(),
            decimal.Round(days.Sum(d => d.Hours ?? 0m), 2),
            DateTime.Now));
    }

    /* =============================================================== rota */

    private static readonly string[] DayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public async Task<IReadOnlyList<AttendanceShiftDto>> GetShiftsAsync(CancellationToken ct = default) =>
        await _db.AttendanceShifts.AsNoTracking()
            .OrderBy(s => s.StartTime)
            .Select(s => new AttendanceShiftDto(
                s.Id, s.Name, s.StartTime, s.EndTime, s.ScanOutBy, s.IsActive))
            .ToListAsync(ct);

    /// <summary>
    /// Everyone who could be rostered, whether or not they are — a person with no
    /// lines yet is exactly who the administrator has opened the screen to add.
    /// </summary>
    public async Task<IReadOnlyList<StaffRotaDto>> GetRotaAsync(
        string? staffType, CancellationToken ct = default)
    {
        var wanted = string.IsNullOrWhiteSpace(staffType) ? null : staffType.Trim();

        var shifts = await _db.AttendanceShifts.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var assignments = await _db.StaffShiftAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(ct);

        var logins = await _db.Users.AsNoTracking()
            .Where(u => u.Email != null)
            .Select(u => u.Email!.ToLower())
            .ToListAsync(ct);

        var loginSet = logins.ToHashSet();

        var staff = new List<StaffRotaDto>();

        if (wanted is null or "Receptionist")
        {
            var receptionists = await _db.Receptionists.AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.Name, r.Branch, r.LoginId })
                .ToListAsync(ct);

            staff.AddRange(receptionists.Select(r => new StaffRotaDto(
                "Receptionist", r.Id, r.Name, r.Branch,
                r.LoginId != null && loginSet.Contains(r.LoginId.ToLower()),
                LinesFor("Receptionist", r.Id))));
        }

        if (wanted is null or "Teacher")
        {
            var teachers = await _db.Teachers.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.LoginId })
                .ToListAsync(ct);

            staff.AddRange(teachers.Select(t => new StaffRotaDto(
                "Teacher", t.Id, t.Name ?? "(unnamed)", null,
                t.LoginId != null && loginSet.Contains(t.LoginId.ToLower()),
                LinesFor("Teacher", t.Id))));
        }

        return staff;

        IReadOnlyList<RotaLineDto> LinesFor(string type, int id) =>
            assignments
                .Where(a => a.StaffType == type && a.StaffId == id)
                .OrderBy(a => a.DayOfWeek)
                .Select(a => new RotaLineDto(
                    a.DayOfWeek, DayNames[a.DayOfWeek], a.ShiftId,
                    shifts.TryGetValue(a.ShiftId, out var name) ? name : "(unknown)"))
                .ToList();
    }

    public async Task<Result<bool>> SaveRotaAsync(
        SaveRotaRequest request, string? actor, CancellationToken ct = default)
    {
        var type = request.StaffType.Trim();

        var exists = type == "Teacher"
            ? await _db.Teachers.AnyAsync(t => t.Id == request.StaffId, ct)
            : await _db.Receptionists.AnyAsync(r => r.Id == request.StaffId, ct);

        if (!exists)
            return Result<bool>.Failure("That member of staff was not found.", ResultStatus.NotFound);

        var shiftIds = request.Lines.Select(l => l.ShiftId).Distinct().ToList();

        var known = await _db.AttendanceShifts
            .Where(s => shiftIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (known.Count != shiftIds.Count)
            return Result<bool>.Failure("One of those shifts no longer exists.");

        /* Replaced wholesale rather than merged. The screen sends the rota it is
           showing, so what is stored afterwards is exactly what the administrator
           was looking at when they pressed save — no half-applied edit, and no line
           surviving because the removal was missed. */
        var current = await _db.StaffShiftAssignments
            .Where(a => a.StaffType == type && a.StaffId == request.StaffId)
            .ToListAsync(ct);

        _db.StaffShiftAssignments.RemoveRange(current);

        var now = DateTime.Now;

        foreach (var line in request.Lines.DistinctBy(l => (l.DayOfWeek, l.ShiftId)))
        {
            _db.StaffShiftAssignments.Add(new StaffShiftAssignment
            {
                StaffType = type,
                StaffId = request.StaffId,
                DayOfWeek = line.DayOfWeek,
                ShiftId = line.ShiftId,
                IsActive = true,
                CreatedBy = actor,
                CreatedOn = now
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rota for {StaffType} {StaffId} set to {Count} line(s) by {Actor}",
            type, request.StaffId, request.Lines.Count, actor);

        return Result<bool>.Success(true);
    }

    /* ============================================================ helpers */

    private sealed record StaffRef(string Type, int Id, string Name, string? PersonalEmail);

    /// <summary>One line of the rota, flattened for the deadline calculation.</summary>
    private sealed record RotaEntry(byte DayOfWeek, TimeOnly StartTime, TimeOnly ScanOutBy);

    /// <summary>
    /// When an unmatched arrival stops being an open shift and becomes leave.
    /// </summary>
    /// <remarks>
    /// <para>A rostered person is judged against the clock: the morning shift says
    /// scan out by 15:00 and the evening by 23:00, whatever time they arrived.</para>
    ///
    /// <para>Which of the day's shifts applies is decided by when they scanned in —
    /// the latest one that had already started, falling back to the day's first if
    /// they arrived before it. Ishani works both shifts on a Sunday, so arriving at
    /// 09:50 is a morning (out by 15:00) and arriving at 17:05 is an evening (out by
    /// 23:00); the same person, the same day, two different deadlines.</para>
    ///
    /// <para>Anyone not on the rota for that weekday — every teacher, and a
    /// receptionist covering an unrostered day — falls back to the flat eight hours
    /// from their own arrival. That is the automatic rule, which the owner scoped to
    /// teachers; it also keeps a stand-in from being locked out.</para>
    /// </remarks>
    private DateTime DeadlineFor(
        IReadOnlyList<RotaEntry> rota, DateOnly onDate, DateTime arrivedAt)
    {
        var today = rota
            .Where(r => r.DayOfWeek == (byte)onDate.DayOfWeek)
            .OrderBy(r => r.StartTime)
            .ToList();

        if (today.Count == 0)
            return arrivedAt.AddHours(Math.Max(1, _options.AutoLeaveAfterHours));

        var arrivedTime = TimeOnly.FromDateTime(arrivedAt);

        var shift = today.LastOrDefault(r => r.StartTime <= arrivedTime) ?? today[0];

        /* A deadline that has already gone past midnight belongs to the next day —
           an evening shift ending at 23:00 is same-day, but this keeps a later
           closing hour from landing in the past. */
        var deadline = onDate.ToDateTime(shift.ScanOutBy);

        return deadline < arrivedAt ? deadline.AddDays(1) : deadline;
    }

    /// <summary>The rota for one person, or empty when they are not on it.</summary>
    private Task<List<RotaEntry>> RotaForAsync(string staffType, int staffId, CancellationToken ct) =>
        _db.StaffShiftAssignments
            .AsNoTracking()
            .Where(a => a.StaffType == staffType && a.StaffId == staffId && a.IsActive)
            .Join(_db.AttendanceShifts.Where(s => s.IsActive),
                  a => a.ShiftId, s => s.Id,
                  (a, s) => new RotaEntry(a.DayOfWeek, s.StartTime, s.ScanOutBy))
            .ToListAsync(ct);

    /// <summary>
    /// Who is scanning. A teacher is matched on <c>Teacher.LoginId</c> and a
    /// receptionist on <c>Receptionist.LoginId</c>; both are compared without case,
    /// because the migrated data holds sign-ins in mixed case.
    /// </summary>
    private async Task<StaffRef?> ResolveStaffAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var login = email.Trim();

        var teacher = await _db.Teachers.AsNoTracking()
            .Where(t => t.LoginId != null && t.LoginId.ToLower() == login.ToLower())
            .Select(t => new { t.Id, t.Name, t.PersonalEmail })
            .FirstOrDefaultAsync(ct);

        if (teacher is not null)
            return new StaffRef("Teacher", teacher.Id, teacher.Name ?? login, teacher.PersonalEmail);

        var receptionist = await _db.Receptionists.AsNoTracking()
            .Where(r => r.LoginId != null && r.LoginId.ToLower() == login.ToLower())
            .Select(r => new { r.Id, r.Name, r.PersonalEmail })
            .FirstOrDefaultAsync(ct);

        return receptionist is null
            ? null
            : new StaffRef("Receptionist", receptionist.Id, receptionist.Name, receptionist.PersonalEmail);
    }

    /// <summary>
    /// Hours from complete pairs only. An unmatched <c>In</c> at the end of the day
    /// contributes nothing — the person did not scan out, so the time they left is
    /// not known, and inventing one would put a number in a report that nobody
    /// measured. The report counts those days separately instead.
    /// </summary>
    private static decimal? HoursFrom(IReadOnlyList<StaffAttendance> scans)
    {
        decimal minutes = 0;
        DateTime? openedAt = null;

        foreach (var scan in scans.OrderBy(s => s.ScannedOn))
        {
            if (scan.Direction == "In")
            {
                openedAt = scan.ScannedOn;
            }
            else if (openedAt is not null)
            {
                minutes += (decimal)(scan.ScannedOn - openedAt.Value).TotalMinutes;
                openedAt = null;
            }
        }

        return minutes <= 0 ? null : decimal.Round(minutes / 60m, 2);
    }

    /// <summary>
    /// Pairs each In with the Out that follows it, same walk as <see cref="HoursFrom"/>
    /// but keeping every pair rather than only their total -- this is what lets a
    /// report tell "here from 9 to 6" apart from "in for two hours, gone, back for
    /// five minutes", which sum to the same total but are not the same day.
    /// </summary>
    private static List<AttendanceVisitDto> VisitsFrom(IReadOnlyList<StaffAttendance> scans)
    {
        var visits = new List<AttendanceVisitDto>();
        DateTime? openedAt = null;

        foreach (var scan in scans.OrderBy(s => s.ScannedOn))
        {
            if (scan.Direction == "In")
            {
                if (openedAt is not null) visits.Add(new AttendanceVisitDto(openedAt.Value, null, null));
                openedAt = scan.ScannedOn;
            }
            else if (openedAt is not null)
            {
                var hours = decimal.Round((decimal)(scan.ScannedOn - openedAt.Value).TotalMinutes / 60m, 2);
                visits.Add(new AttendanceVisitDto(openedAt.Value, scan.ScannedOn, hours));
                openedAt = null;
            }
        }

        if (openedAt is not null) visits.Add(new AttendanceVisitDto(openedAt.Value, null, null));

        return visits;
    }

    /// <summary>
    /// Great-circle distance in metres. The haversine formula, which is accurate to
    /// a metre or two at these ranges and needs no spatial types in the database.
    /// </summary>
    private static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMetres = 6_371_000d;

        var dLat = (lat2 - lat1) * Math.PI / 180d;
        var dLon = (lon2 - lon1) * Math.PI / 180d;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180d) * Math.Cos(lat2 * Math.PI / 180d)
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusMetres * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static DateOnly PeriodStartOf(DateOnly date, AttendanceGrouping grouping) => grouping switch
    {
        AttendanceGrouping.Year => new DateOnly(date.Year, 1, 1),
        AttendanceGrouping.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date
    };

    private static string PeriodLabelOf(DateOnly start, AttendanceGrouping grouping) => grouping switch
    {
        AttendanceGrouping.Year => start.Year.ToString(CultureInfo.InvariantCulture),
        AttendanceGrouping.Month => start.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
        _ => start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Days of the period that fall inside the range asked for, so a part month is
    /// not marked absent for the days nobody was expected to work yet.
    /// </summary>
    private static int DaysInPeriod(
        DateOnly periodStart, AttendanceGrouping grouping, DateOnly from, DateOnly to)
    {
        var periodEnd = grouping switch
        {
            AttendanceGrouping.Year => periodStart.AddYears(1).AddDays(-1),
            AttendanceGrouping.Month => periodStart.AddMonths(1).AddDays(-1),
            _ => periodStart
        };

        var start = periodStart < from ? from : periodStart;
        var end = periodEnd > to ? to : periodEnd;

        return Math.Max(0, end.DayNumber - start.DayNumber + 1);
    }
}
