using System.Globalization;
using System.Security.Cryptography;
using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Competitions;
using InstituteMS.Application.Notifications;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The yearly Drawing &amp; Dance competition: the public registration and the
/// office's side of it.
/// </summary>
public class CompetitionService : ICompetitionService
{
    /// <summary>A document larger than this is a camera file nobody resized.</summary>
    private const int MaxDocumentBytes = 3 * 1024 * 1024;

    /// <summary>
    /// A poster may be bigger than a school id card: it is artwork, shown full
    /// width, and the small print on it has to stay readable.
    /// </summary>
    private const int MaxAdvertBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly PaymentSettings _payments;
    private readonly IChestCardPdfGenerator _chestCards;
    private readonly IPasswordHasher _hasher;
    private readonly IStudentMailer _mailer;
    private readonly ILogger<CompetitionService> _logger;

    public CompetitionService(
        AppDbContext db,
        IOptions<PaymentSettings> payments,
        IChestCardPdfGenerator chestCards,
        IPasswordHasher hasher,
        IStudentMailer mailer,
        ILogger<CompetitionService> logger)
    {
        _db = db;
        _payments = payments.Value;
        _chestCards = chestCards;
        _hasher = hasher;
        _mailer = mailer;
        _logger = logger;
    }

    /* ============================================================== public */

    public async Task<CompetitionPublicDto?> GetPublicAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        /* The newest active year, whether or not it is open for registration: a
           visitor who arrives the day after closing should still see what happened
           rather than an empty page. IsOpen below decides whether the form opens. */
        var competition = await _db.Competitions
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.Year)
            .FirstOrDefaultAsync(ct);

        if (competition is null) return null;

        var groups = await _db.CompetitionGroups
            .AsNoTracking()
            .Where(g => g.CompetitionId == competition.Id)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

        var organisers = await _db.CompetitionOrganisers
            .AsNoTracking()
            .Where(o => o.CompetitionId == competition.Id && o.IsActive)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .ToListAsync(ct);

        var advertSlots = await _db.CompetitionAdverts
            .AsNoTracking()
            .Where(a => a.CompetitionId == competition.Id)
            .Select(a => a.Slot)
            .ToListAsync(ct);

        return Describe(competition, groups, organisers, today, advertSlots);
    }

    private CompetitionPublicDto Describe(
        Competition c,
        IReadOnlyList<CompetitionGroup> groups,
        IReadOnlyList<CompetitionOrganiser> organisers,
        DateOnly today,
        IReadOnlyCollection<string> advertSlots)
    {
        /* Open only inside the window the administrator set. A missing date on
           either side means "not restricted on that side"; missing on both means
           the year has been set up but registration has not been opened, which is
           deliberately closed rather than deliberately open — an event that starts
           advertising itself the moment somebody creates the row would be worse. */
        var isOpen = c.IsActive
            && c.RegistrationOpensOn is not null
            && today >= c.RegistrationOpensOn
            && (c.RegistrationClosesOn is null || today <= c.RegistrationClosesOn);

        static CompetitionGroupDto Map(CompetitionGroup g) => new(
            g.Id, g.Kind, g.Code, g.LabelEnglish, g.LabelBengali,
            g.MinAge, g.MaxAge, g.MinClass, g.MaxClass, g.Capacity, g.SortOrder);

        return new CompetitionPublicDto(
            c.Id, c.Year, c.EventDate, c.Venue, c.VenueBengali,
            c.DrawingFee, c.DanceFee,
            c.DrawingStartTime, c.DanceStartTime, c.PrizeGivingTime,
            c.NoticeEnglish, c.NoticeBengali,
            c.DrawingRulesEnglish, c.DrawingRulesBengali,
            c.DanceRulesEnglish, c.DanceRulesBengali,
            c.ContactPhones,

            /* The same UPI id the student fee page pays into. Sent to the portal so
               it can draw a QR carrying the exact amount, rather than showing a
               static code and asking a parent to type Rs 140 themselves. */
            _payments.UpiId,
            _payments.PayeeName,

            isOpen, c.RegistrationOpensOn, c.RegistrationClosesOn,
            groups.Where(g => g.Kind == "Drawing").Select(Map).ToList(),
            groups.Where(g => g.Kind == "Dance").Select(Map).ToList(),
            organisers
                .Select(o => new CompetitionOrganiserDto(
                    o.Id, o.Name, o.Designation, o.Phone, o.SortOrder, o.IsActive))
                .ToList(),

            advertSlots.Contains("Drawing"),
            advertSlots.Contains("Dance"));
    }

    /* ========================================================= registering */

    public async Task<Result<CompetitionRegisteredDto>> RegisterAsync(
        CompetitionRegisterRequest request, string? sourceIp, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var competition = await _db.Competitions
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.Year)
            .FirstOrDefaultAsync(ct);

        if (competition is null)
            return Fail("No competition is open for registration.");

        var open = competition.RegistrationOpensOn is not null
            && today >= competition.RegistrationOpensOn
            && (competition.RegistrationClosesOn is null || today <= competition.RegistrationClosesOn);

        if (!open)
            return Fail("Registration is closed.");

        if (!request.RulesAccepted)
            return Fail("Please read and accept the rules before registering.");

        if (request.DrawingGroupId is null && request.DanceGroupId is null)
            return Fail("Choose the drawing competition, the dance competition, or both.");

        var wantsDrawing = request.DrawingGroupId is > 0;
        var wantsDance = request.DanceGroupId is > 0;

        /* --------------------------------------------------- the divisions */

        var groups = await _db.CompetitionGroups
            .AsNoTracking()
            .Where(g => g.CompetitionId == competition.Id)
            .ToListAsync(ct);

        CompetitionGroup? drawingGroup = null;
        CompetitionGroup? danceGroup = null;

        if (wantsDrawing)
        {
            drawingGroup = groups.FirstOrDefault(
                g => g.Id == request.DrawingGroupId && g.Kind == "Drawing");

            if (drawingGroup is null)
                return Fail("Choose a drawing division from the list.");

            /* The drawing divisions are school-class brackets the same way the dance
               groups are age brackets, and the owner asked that a class be checked
               against the division chosen the same way. There is no way to read the
               class off the uploaded school id photograph itself -- nothing in this
               system reads an image -- so the class is typed in and checked against
               the bracket instead; the id card stays required as the proof of it. */
            if (string.IsNullOrWhiteSpace(request.ClassName))
                return Fail("Enter the participant's school class — it decides the drawing division.");

            var schoolClass = ParseSchoolClass(request.ClassName);

            if (schoolClass is null)
            {
                return Fail(
                    "The school class was not understood. Enter it plainly, such as " +
                    "\"Nursery\", \"V\" or \"5\".");
            }

            if (schoolClass < drawingGroup.MinClass || schoolClass > drawingGroup.MaxClass)
            {
                return Fail(
                    $"A student in {SchoolClassLabel(schoolClass.Value)} does not fall in " +
                    $"{drawingGroup.LabelEnglish}. Choose the division that matches their class.");
            }
        }

        if (wantsDance)
        {
            danceGroup = groups.FirstOrDefault(
                g => g.Id == request.DanceGroupId && g.Kind == "Dance");

            if (danceGroup is null)
                return Fail("Choose a dance group from the list.");

            /* The dance groups are age brackets and the poster already warns that
               age proof may be asked for, so an entry in the wrong bracket is
               caught here rather than at the venue on the morning. */
            if (request.DateOfBirth is { } dob && danceGroup.MinAge is not null)
            {
                var age = AgeOn(dob, competition.EventDate ?? today);

                if (age < danceGroup.MinAge || (danceGroup.MaxAge is not null && age > danceGroup.MaxAge))
                {
                    return Fail(
                        $"At {age} the participant does not fall in {danceGroup.LabelEnglish}. " +
                        "Choose the group that matches their age.");
                }
            }
            else if (danceGroup.MinAge is not null)
            {
                /* The dance groups are age brackets and the birth certificate is
                   what proves the age, so an entry with no date of birth cannot be
                   placed in one at all. */
                return Fail("Enter the date of birth — it decides the dance group.");
            }

            /* Places are limited: 30, 30 and 15. Checked here for a civil message,
               and again inside the transaction below — between this line and the
               insert, somebody else may take the last place. */
            if (danceGroup.Capacity is { } capacity)
            {
                // Only entries still holding a chest number occupy a place: a
                // cancelled one gave both back.
                var taken = await _db.CompetitionEntries
                    .CountAsync(e => e.GroupId == danceGroup.Id && e.BatchNo != null, ct);

                if (taken >= capacity)
                {
                    return Fail(
                        $"{danceGroup.LabelEnglish} is full — all {capacity} places have been taken. " +
                        "Please telephone the centre.");
                }
            }
        }

        /* ----------------------------------------------------- the payment */

        var amount = (wantsDrawing ? competition.DrawingFee : 0m)
                   + (wantsDance ? competition.DanceFee : 0m);

        var mode = string.Equals(request.PaymentMode, "Venue", StringComparison.OrdinalIgnoreCase)
            ? "Venue"
            : "Online";

        /* Paying on arrival is for drawing alone. The owner was explicit: "offline
           means they are paying their amount after reaching in program venue but
           this will be applicable only for drawing only for dance online payment is
           must". */
        if (mode == "Venue" && wantsDance)
            return Fail("Dance registration must be paid online. Only drawing may be paid at the venue.");

        var reference = request.TransactionRef?.Trim();

        if (mode == "Online")
        {
            if (string.IsNullOrWhiteSpace(reference))
                return Fail("Enter the UPI reference number from your payment app.");

            if (reference.Length < 6)
                return Fail("A UPI reference is at least six characters.");

            /* The one verification that is actually possible. Nothing here can ask
               the bank whether the money arrived — the QR is static and no gateway
               reports back — but the same reference being submitted by two entrants
               is the obvious cheat, and it is refused. An administrator confirms the
               rest from the dashboard. */
            var used = await _db.CompetitionApplicants
                .AnyAsync(a => a.TransactionRef == reference, ct);

            if (used)
                return Fail("That UPI reference has already been used for another registration.");

            if (request.PaymentSlip is null)
                return Fail("Upload the payment screenshot.");
        }
        else
        {
            reference = null;
        }

        /* ---------------------------------------------------- the documents */

        var documents = new List<CompetitionDocument>();

        var photo = Decode(request.Photo, "Photo");
        if (!photo.Succeeded) return Fail(photo.Error!);
        if (photo.Value is null) return Fail("Upload the participant's photograph.");
        documents.Add(photo.Value);

        if (wantsDrawing)
        {
            var schoolId = Decode(request.SchoolId, "SchoolId");
            if (!schoolId.Succeeded) return Fail(schoolId.Error!);
            if (schoolId.Value is null) return Fail("Upload the school id card for the drawing competition.");
            documents.Add(schoolId.Value);
        }

        if (wantsDance)
        {
            var birth = Decode(request.BirthCertificate, "BirthCertificate");
            if (!birth.Succeeded) return Fail(birth.Error!);
            if (birth.Value is null) return Fail("Upload the birth certificate for the dance competition.");
            documents.Add(birth.Value);
        }

        if (mode == "Online")
        {
            var slip = Decode(request.PaymentSlip, "PaymentSlip");
            if (!slip.Succeeded) return Fail(slip.Error!);
            if (slip.Value is not null) documents.Add(slip.Value);
        }

        /* ------------------------------------------------------- the entry */

        var applicant = new CompetitionApplicant
        {
            CompetitionId = competition.Id,
            ParticipantName = request.ParticipantName.Trim(),
            GuardianName = Blank(request.GuardianName),
            DateOfBirth = request.DateOfBirth,
            SchoolName = Blank(request.SchoolName),
            ClassName = Blank(request.ClassName),
            Phone = request.Phone.Trim(),
            Email = Blank(request.Email),
            Address = Blank(request.Address),
            PaymentMode = mode,
            Amount = amount,
            TransactionRef = reference,
            PaymentStatus = mode == "Venue" ? "AtVenue" : "Pending",
            SubmittedOn = DateTime.Now,
            SourceIp = sourceIp
        };

        var strategy = _db.Database.CreateExecutionStrategy();

        List<CompetitionEntry> result;

        try
        {
            result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.CompetitionApplicants.Add(applicant);
            await _db.SaveChangesAsync(ct);

            foreach (var document in documents)
            {
                document.ApplicantId = applicant.Id;
                document.UploadedOn = DateTime.Now;
                _db.CompetitionDocuments.Add(document);
            }

            var entries = new List<CompetitionEntry>();

            if (wantsDrawing)
            {
                entries.Add(await NextEntryAsync(
                    competition, applicant.Id, "Drawing", drawingGroup!.Id, null, ct));
            }

            if (wantsDance)
            {
                entries.Add(await NextEntryAsync(
                    competition, applicant.Id, "Dance", danceGroup!.Id, danceGroup.Capacity, ct));
            }

            _db.CompetitionEntries.AddRange(entries);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return entries;
            });
        }
        catch (CompetitionGroupFullException full)
        {
            // Somebody took the last place between the check above and the insert.
            return Fail(
                $"That dance group filled up while you were registering — all {full.Capacity} " +
                "places have been taken. Please telephone the centre.");
        }

        /* If this submission continued a save-and-resume draft, the draft is done —
           linked to the real registration it became, and out of the office's
           follow-up list. Best-effort: a draft that cannot be matched (a typo'd
           code, one already linked) does not fail the registration itself. */
        if (!string.IsNullOrWhiteSpace(request.DraftCode))
        {
            var draft = await _db.CompetitionDrafts
                .FirstOrDefaultAsync(d => d.RegistrationCode == request.DraftCode.Trim(), ct);

            if (draft is not null && draft.Status == "Draft")
            {
                draft.Status = "Completed";
                draft.LinkedApplicantId = applicant.Id;
                await _db.SaveChangesAsync(ct);
            }
        }

        _logger.LogInformation(
            "Competition {Year}: {Name} registered for {Kinds}, {Amount} by {Mode}",
            competition.Year, applicant.ParticipantName,
            string.Join(" and ", result.Select(e => e.Kind)), amount, mode);

        var described = result.Select(e =>
        {
            var group = groups.FirstOrDefault(g => g.Id == e.GroupId);

            return new CompetitionEntryDto(
                e.Kind, e.RegistrationNo, group?.Code, group?.LabelEnglish, e.BatchNo);
        }).ToList();

        var message = mode == "Venue"
            ? "Registered. Please pay at the venue on the day of the programme."
            : "Registered. Your payment will be confirmed by the office once it is checked.";

        return Result<CompetitionRegisteredDto>.Success(new CompetitionRegisteredDto(
            applicant.Id, applicant.ParticipantName, amount, mode,
            applicant.PaymentStatus, described, message));
    }

    /* ======================================================= save and resume */

    public async Task<Result<CompetitionDraftStartedDto>> StartDraftAsync(
        CompetitionDraftStartRequest request, CancellationToken ct = default)
    {
        var name = request.ParticipantName?.Trim();
        var phone = request.Phone?.Trim();
        var email = request.Email?.Trim();
        var password = request.Password;

        if (string.IsNullOrWhiteSpace(name))
            return Result<CompetitionDraftStartedDto>.Failure("Enter the participant's name.");

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 10)
            return Result<CompetitionDraftStartedDto>.Failure("Enter a valid phone number.");

        // Required since this is the only way "forgot password" has anywhere to send
        // a reset code — a phone number alone gives no such address.
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            return Result<CompetitionDraftStartedDto>.Failure("Enter a valid email address.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return Result<CompetitionDraftStartedDto>.Failure("Choose a password at least 6 characters long.");

        var today = DateOnly.FromDateTime(DateTime.Today);

        var competition = await _db.Competitions
            .Where(c => c.IsActive
                        && c.RegistrationOpensOn != null && c.RegistrationOpensOn <= today
                        && c.RegistrationClosesOn != null && c.RegistrationClosesOn >= today)
            .OrderByDescending(c => c.Year)
            .FirstOrDefaultAsync(ct);

        if (competition is null)
            return Result<CompetitionDraftStartedDto>.Failure("Registration is not currently open.");

        var draft = new CompetitionDraft
        {
            CompetitionId = competition.Id,
            ParticipantName = name,
            Phone = phone,
            Email = email,
            PasswordHash = _hasher.Hash(password),
            Status = "Draft",
            CreatedOn = DateTime.Now
        };

        _db.CompetitionDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        // Derived from the row's own identity, same reason as every other number
        // in this module — see MonthlyFee.BillNo for why not MAX(id) + 1.
        draft.RegistrationCode = $"AVI/CMP/{competition.Year}/{draft.Id:0000}";
        await _db.SaveChangesAsync(ct);

        /* Recorded on the dashboard before anything else, the same way a password
           change is — the alert survives even if nobody reads it for a week, and it
           is what lets the office follow up by phone with someone who started but
           never came back. The password travels here in clear, the same precedent
           StudentPasswordChange already set: the office has to be able to read it
           back to someone who calls in having forgotten it. */
        _db.AdminNotifications.Add(new AdminNotification
        {
            Type = AdminNotificationTypes.CompetitionDraftStarted,
            Title = $"{name} started a Drawing & Dance registration ({draft.RegistrationCode})",
            StudentName = name,
            RegistrationId = draft.RegistrationCode,
            LoginId = phone,
            DetailValue = password,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Competition draft {Code} started by {Name}", draft.RegistrationCode, name);

        return Result<CompetitionDraftStartedDto>.Success(
            new CompetitionDraftStartedDto(draft.RegistrationCode, name));
    }

    public async Task<Result<CompetitionDraftDto>> ResumeDraftAsync(
        CompetitionDraftLoginRequest request, CancellationToken ct = default)
    {
        var code = request.RegistrationCode?.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Password))
            return Result<CompetitionDraftDto>.Failure("Enter your registration id and password.");

        var draft = await _db.CompetitionDrafts.FirstOrDefaultAsync(d => d.RegistrationCode == code, ct);

        // One message for "no such code" and "wrong password" -- same reason the
        // chest-card lookup does this: a registration code must not be a thing that
        // can be walked by trying numbers.
        if (draft is null || !_hasher.Verify(request.Password, draft.PasswordHash))
        {
            return Result<CompetitionDraftDto>.Failure(
                "No registration matches that id and password.", ResultStatus.NotFound);
        }

        draft.LastAccessedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<CompetitionDraftDto>.Success(new CompetitionDraftDto(
            draft.RegistrationCode, draft.CompetitionId, draft.ParticipantName, draft.Phone, draft.Status));
    }

    public async Task<Result<bool>> ChangeDraftPasswordAsync(
        CompetitionDraftChangePasswordRequest request, CancellationToken ct = default)
    {
        var code = request.RegistrationCode?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return Result<bool>.Failure("Enter your registration id.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return Result<bool>.Failure("Choose a new password at least 6 characters long.");

        var draft = await _db.CompetitionDrafts.FirstOrDefaultAsync(d => d.RegistrationCode == code, ct);

        if (draft is null || !_hasher.Verify(request.CurrentPassword, draft.PasswordHash))
        {
            return Result<bool>.Failure(
                "No registration matches that id and password.", ResultStatus.NotFound);
        }

        draft.PasswordHash = _hasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync(ct);

        // Same precedent as StartDraftAsync and StudentPasswordChange: the office
        // has to be able to read a registrant's password back to them if they call
        // in having forgotten it again, so it travels here in clear too.
        _db.AdminNotifications.Add(new AdminNotification
        {
            Type = AdminNotificationTypes.CompetitionDraftPasswordChange,
            Title = $"{draft.ParticipantName} changed their registration password ({draft.RegistrationCode})",
            StudentName = draft.ParticipantName,
            RegistrationId = draft.RegistrationCode,
            LoginId = draft.Phone,
            DetailValue = request.NewPassword,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    private const int DraftResetCodeValidMinutes = 15;
    private const int MaxDraftResetCodeAttempts = 5;

    public async Task<Result<bool>> ForgotDraftPasswordAsync(
        CompetitionDraftForgotPasswordRequest request, CancellationToken ct = default)
    {
        var code = request.RegistrationCode?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return Result<bool>.Success(true);

        var draft = await _db.CompetitionDrafts.FirstOrDefaultAsync(d => d.RegistrationCode == code, ct);

        /* Whether that code matched anything, and whether it has an email on file, is
           not this response's to reveal — success either way, same reasoning as
           AuthService.ForgotPasswordAsync. A draft started before Email was required
           has nothing to send to; the office can still help by phone, from the same
           dashboard row every draft already lands on. */
        if (draft is null || string.IsNullOrWhiteSpace(draft.Email))
            return Result<bool>.Success(true);

        var resetCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;

        draft.PasswordResetCodeHash = _hasher.Hash(resetCode);
        draft.PasswordResetCodeExpiry = now.AddMinutes(DraftResetCodeValidMinutes);
        draft.PasswordResetAttempts = 0;

        await _db.SaveChangesAsync(ct);

        var sent = await _mailer.SendPasswordResetCodeAsync(
            new PasswordResetCodeMailRequest(draft.ParticipantName, draft.Email, resetCode, DraftResetCodeValidMinutes),
            draft.Phone, ct);

        if (!sent)
        {
            _logger.LogWarning(
                "Password reset code could not be sent to {Email} for draft {Code}.", draft.Email, draft.RegistrationCode);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ResetDraftPasswordAsync(
        CompetitionDraftResetPasswordRequest request, CancellationToken ct = default)
    {
        const string invalidOrExpired = "That code is invalid or has expired. Request a new one.";

        var code = request.RegistrationCode?.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return Result<bool>.Failure(invalidOrExpired);

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return Result<bool>.Failure("Choose a new password at least 6 characters long.");

        var draft = await _db.CompetitionDrafts.FirstOrDefaultAsync(d => d.RegistrationCode == code, ct);

        if (draft is null)
        {
            _hasher.Verify(request.Code, null);
            return Result<bool>.Failure(invalidOrExpired);
        }

        var now = DateTime.UtcNow;

        if (draft.PasswordResetCodeHash is null || draft.PasswordResetCodeExpiry is null || draft.PasswordResetCodeExpiry < now)
        {
            _hasher.Verify(request.Code, null);
            return Result<bool>.Failure(invalidOrExpired);
        }

        if (draft.PasswordResetAttempts >= MaxDraftResetCodeAttempts)
            return Result<bool>.Failure("Too many wrong codes. Request a new one.", ResultStatus.TooManyRequests);

        if (!_hasher.Verify(request.Code, draft.PasswordResetCodeHash))
        {
            draft.PasswordResetAttempts++;
            await _db.SaveChangesAsync(ct);
            return Result<bool>.Failure(invalidOrExpired);
        }

        draft.PasswordHash = _hasher.Hash(request.NewPassword);

        // Spent — a code is good for one reset, not for the fifteen minutes it is valid.
        draft.PasswordResetCodeHash = null;
        draft.PasswordResetCodeExpiry = null;
        draft.PasswordResetAttempts = 0;

        await _db.SaveChangesAsync(ct);

        // Same office notification every other password change on a draft already raises.
        _db.AdminNotifications.Add(new AdminNotification
        {
            Type = AdminNotificationTypes.CompetitionDraftPasswordChange,
            Title = $"{draft.ParticipantName} reset their registration password by email ({draft.RegistrationCode})",
            StudentName = draft.ParticipantName,
            RegistrationId = draft.RegistrationCode,
            LoginId = draft.Phone,
            DetailValue = request.NewPassword,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task<Result<IReadOnlyList<CompetitionDraftRowDto>>> GetDraftsAsync(
        int? competitionId, CancellationToken ct = default)
    {
        var drafts = _db.CompetitionDrafts.AsNoTracking().AsQueryable();

        if (competitionId is > 0)
            drafts = drafts.Where(d => d.CompetitionId == competitionId);

        var rows = await drafts
            .OrderByDescending(d => d.CreatedOn)
            .Select(d => new CompetitionDraftRowDto(
                d.RegistrationCode, d.ParticipantName, d.Phone, d.Status,
                d.CreatedOn, d.LastAccessedOn))
            .ToListAsync(ct);

        return Result<IReadOnlyList<CompetitionDraftRowDto>>.Success(rows);
    }

    /// <summary>
    /// The next number for one competition in one year — <c>AVI/DRW/2027/001</c>.
    /// </summary>
    /// <remarks>
    /// Taken as MAX + 1 inside the caller's transaction, with a unique index on
    /// (competition, kind, serial) behind it. Two forms submitted in the same second
    /// therefore cannot be issued the same number: the second insert fails on the
    /// index rather than quietly duplicating, which is the failure that matters when
    /// the number is what an entrant brings on the day.
    /// </remarks>
    private async Task<CompetitionEntry> NextEntryAsync(
        Competition competition, int applicantId, string kind, int groupId,
        int? capacity, CancellationToken ct)
    {
        var last = await _db.CompetitionEntries
            .Where(e => e.CompetitionId == competition.Id && e.Kind == kind)
            .MaxAsync(e => (int?)e.Serial, ct) ?? 0;

        var serial = last + 1;
        var tag = kind == "Drawing" ? "DRW" : "DNC";

        /* The chest number, for dance only: 1 upwards within the group, which is
           what the dancer prints and pins on. Drawing carries none — the owner was
           clear that a registration number is all it needs.

           Taken as MAX + 1 within the group, inside the caller's transaction, with a
           unique index on (GroupId, BatchNo) behind it: two forms arriving together
           cannot be given the same number, because the second insert fails on the
           index rather than quietly issuing a duplicate. Two dancers wearing 7 is
           precisely the failure worth paying a transaction for. */
        int? batchNo = null;

        if (kind == "Dance")
        {
            var takenNow = await _db.CompetitionEntries
                .CountAsync(e => e.GroupId == groupId && e.BatchNo != null, ct);

            if (capacity is { } limit && takenNow >= limit)
                throw new CompetitionGroupFullException(limit);

            /* The LOWEST free number, not MAX + 1.

               A cancelled entry gives its number back, and MAX + 1 would then run
               1..14, 16 — a pile of printed chest numbers that no longer matches the
               register, and a group of fifteen dancers whose highest number is
               sixteen. Taking the lowest gap keeps every group reading 1..n. */
            var used = await _db.CompetitionEntries
                .Where(e => e.GroupId == groupId && e.BatchNo != null)
                .Select(e => e.BatchNo!.Value)
                .ToListAsync(ct);

            var taken = used.ToHashSet();
            var next = 1;
            while (taken.Contains(next)) next++;

            batchNo = next;
        }

        return new CompetitionEntry
        {
            ApplicantId = applicantId,
            CompetitionId = competition.Id,
            Kind = kind,
            GroupId = groupId,
            Serial = serial,
            RegistrationNo = $"AVI/{tag}/{competition.Year}/{serial:D3}",
            BatchNo = batchNo,
            CreatedOn = DateTime.Now
        };
    }

    /* ============================================================== office */

    public async Task<Result<CompetitionReportDto>> GetReportAsync(
        CompetitionReportQuery query, CancellationToken ct = default)
    {
        var years = await _db.Competitions
            .AsNoTracking()
            .OrderByDescending(c => c.Year)
            .Select(c => c.Year)
            .ToListAsync(ct);

        if (years.Count == 0)
            return Result<CompetitionReportDto>.Failure("No competition has been set up yet.");

        var year = query.Year is > 0 && years.Contains(query.Year.Value)
            ? query.Year.Value
            : years[0];

        var competitionId = await _db.Competitions
            .Where(c => c.Year == year)
            .Select(c => c.Id)
            .FirstAsync(ct);

        var q = _db.CompetitionEntries
            .AsNoTracking()
            .Where(e => e.CompetitionId == competitionId);

        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            var kind = query.Kind.Trim();
            q = q.Where(e => e.Kind == kind);
        }

        var rows = await q
            .Select(e => new
            {
                e.ApplicantId,
                e.RegistrationNo,
                e.Kind,
                GroupCode = e.Group != null ? e.Group.Code : null,
                GroupLabel = e.Group != null ? e.Group.LabelEnglish : null,
                e.BatchNo,

                e.Applicant!.ParticipantName,
                e.Applicant.GuardianName,
                e.Applicant.DateOfBirth,
                e.Applicant.SchoolName,
                e.Applicant.ClassName,
                e.Applicant.Phone,
                e.Applicant.Email,
                e.Applicant.Amount,
                e.Applicant.PaymentMode,
                e.Applicant.PaymentStatus,
                e.Applicant.TransactionRef,
                e.Applicant.CancelReason,
                e.Applicant.ConfirmedBy,
                e.Applicant.ConfirmedOn,
                e.Applicant.SubmittedOn,

                Documents = e.Applicant.Documents.Select(d => d.Kind).ToList()
            })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(query.PaymentStatus))
        {
            var status = query.PaymentStatus.Trim();
            rows = rows.Where(r => string.Equals(r.PaymentStatus, status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            rows = rows.Where(r =>
                Contains(r.RegistrationNo, term) ||
                Contains(r.ParticipantName, term) ||
                Contains(r.GuardianName, term) ||
                Contains(r.Phone, term)).ToList();
        }

        var eventDate = await _db.Competitions
            .Where(c => c.Id == competitionId)
            .Select(c => c.EventDate)
            .FirstOrDefaultAsync(ct);

        var reference = eventDate ?? DateOnly.FromDateTime(DateTime.Today);

        var described = rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.RegistrationNo, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CompetitionReportRowDto(
                r.ApplicantId, r.RegistrationNo, r.Kind, r.GroupCode, r.GroupLabel, r.BatchNo,
                r.ParticipantName, r.GuardianName, r.DateOfBirth,
                r.DateOfBirth is { } dob ? AgeOn(dob, reference) : null,
                r.SchoolName, r.ClassName, r.Phone, r.Email,
                r.Amount, r.PaymentMode, r.PaymentStatus, r.TransactionRef,
                r.CancelReason, r.ConfirmedBy, r.ConfirmedOn, r.SubmittedOn,
                r.Documents.Contains("Photo"),
                r.Documents.Contains("SchoolId"),
                r.Documents.Contains("BirthCertificate"),
                r.Documents.Contains("PaymentSlip")))
            .ToList();

        /* Money is counted per applicant, not per entry: a child who entered both
           competitions paid once, and summing the entries would count that payment
           twice. */
        var applicants = described
            .GroupBy(r => r.ApplicantId)
            .Select(g => g.First())
            .ToList();

        return Result<CompetitionReportDto>.Success(new CompetitionReportDto(
            year, years, described,
            described.Count(r => r.Kind == "Drawing"),
            described.Count(r => r.Kind == "Dance"),
            applicants.Count(r => r.PaymentStatus == "Pending"),
            applicants.Where(r => r.PaymentStatus == "Confirmed").Sum(r => r.Amount),
            applicants.Where(r => r.PaymentStatus == "AtVenue").Sum(r => r.Amount),
            DateTime.Now));
    }

    public async Task<Result<bool>> SetPaymentStatusAsync(
        int applicantId, string status, string? actor, CancellationToken ct = default)
    {
        var allowed = new[] { "Pending", "Confirmed", "AtVenue", "Rejected" };

        if (!allowed.Contains(status, StringComparer.OrdinalIgnoreCase))
            return Result<bool>.Failure("That is not a payment status.");

        var applicant = await _db.CompetitionApplicants
            .FirstOrDefaultAsync(a => a.Id == applicantId, ct);

        if (applicant is null)
            return Result<bool>.Failure("That registration no longer exists.", ResultStatus.NotFound);

        applicant.PaymentStatus = allowed.First(a => a.Equals(status, StringComparison.OrdinalIgnoreCase));
        applicant.ConfirmedBy = actor;
        applicant.ConfirmedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Competition registration {Id} marked {Status} by {Actor}",
            applicantId, applicant.PaymentStatus, actor);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Cancels a registration and marks it refunded in full.
    /// </summary>
    /// <remarks>
    /// <para>The owner's rule: "if admin wants admin can cancel registration if
    /// criteria not matching properly, candidate will get refund all amount in that
    /// case for dance and drawing" — the whole amount, both competitions, no partial
    /// refund and no distinction between them.</para>
    ///
    /// <para>Two things go back with it. The dance place returns to its group, and
    /// the chest number is released so the next entrant takes it: without that a
    /// group would sit "full" at fourteen real dancers, and the printed numbers
    /// would run 1..14, 16.</para>
    ///
    /// <para>Nothing is deleted. The entry, the documents and the payment reference
    /// all stay, because a cancellation is a thing that happened and the office may
    /// have to account for the refund later.</para>
    /// </remarks>
    public async Task<Result<bool>> CancelAsync(
        int applicantId, string? reason, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result<bool>.Failure("Say why the registration is being cancelled.");

        var applicant = await _db.CompetitionApplicants
            .Include(a => a.Entries)
            .FirstOrDefaultAsync(a => a.Id == applicantId, ct);

        if (applicant is null)
            return Result<bool>.Failure("That registration no longer exists.", ResultStatus.NotFound);

        if (applicant.PaymentStatus == "Cancelled")
            return Result<bool>.Failure("That registration is already cancelled.");

        applicant.PaymentStatus = "Cancelled";
        applicant.CancelReason = reason.Trim();
        applicant.ConfirmedBy = actor;
        applicant.ConfirmedOn = DateTime.Now;

        foreach (var entry in applicant.Entries)
            entry.BatchNo = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Competition registration {Id} ({Name}) cancelled by {Actor}: {Reason}. " +
            "Refund due {Amount}",
            applicantId, applicant.ParticipantName, actor, applicant.CancelReason, applicant.Amount);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The chest number sheet for one dance registration, as a PDF.
    /// </summary>
    /// <remarks>
    /// <para>Open to the entrant with no account: they give the registration number
    /// and the phone number on their form, which together are what they actually
    /// have. Both must match, so knowing a registration number alone — they run
    /// 001, 002, 003 and are easy to guess — is not enough to pull somebody else's
    /// sheet.</para>
    ///
    /// <para>Refused until the office has confirmed the payment, which is what the
    /// owner asked for: the sheet is the thing that gets a dancer onto the floor, and
    /// it should not exist before the money has been checked.</para>
    /// </remarks>
    public async Task<Result<CompetitionDocumentDto>> GetChestCardAsync(
        string registrationNo, string phone, CancellationToken ct = default)
    {
        var number = registrationNo?.Trim() ?? string.Empty;
        var telephone = phone?.Trim() ?? string.Empty;

        if (number.Length == 0 || telephone.Length == 0)
            return Result<CompetitionDocumentDto>.Failure("Enter your registration number and phone number.");

        var entry = await _db.CompetitionEntries
            .AsNoTracking()
            .Where(e => e.RegistrationNo == number)
            .Select(e => new
            {
                e.Kind,
                e.BatchNo,
                GroupLabel = e.Group != null ? e.Group.LabelEnglish : null,
                e.Applicant!.ParticipantName,
                e.Applicant.Phone,
                e.Applicant.PaymentStatus,
                e.Applicant.Competition!.Year,
                e.Applicant.Competition.EventDate,
                e.Applicant.Competition.Venue
            })
            .FirstOrDefaultAsync(ct);

        /* One message whether the number is unknown or the phone does not match, so
           this cannot be used to find out which registration numbers exist. */
        if (entry is null || !string.Equals(entry.Phone, telephone, StringComparison.Ordinal))
        {
            return Result<CompetitionDocumentDto>.Failure(
                "No registration matches that number and phone number.", ResultStatus.NotFound);
        }

        if (entry.Kind != "Dance" || entry.BatchNo is null)
        {
            return Result<CompetitionDocumentDto>.Failure(
                "A chest number is issued for the dance competition only. " +
                "For drawing, your registration number is all you need on the day.");
        }

        if (entry.PaymentStatus == "Cancelled")
            return Result<CompetitionDocumentDto>.Failure("That registration has been cancelled.");

        if (entry.PaymentStatus != "Confirmed")
        {
            return Result<CompetitionDocumentDto>.Failure(
                "Your payment has not been confirmed by the office yet. " +
                "The chest number can be downloaded once it has been checked.");
        }

        var pdf = _chestCards.Build(new ChestCardDto(
            number, entry.ParticipantName, entry.BatchNo.Value, entry.GroupLabel,
            entry.Year, entry.EventDate, entry.Venue));

        return Result<CompetitionDocumentDto>.Success(new CompetitionDocumentDto(
            pdf, "application/pdf", $"chest-number-{entry.BatchNo}.pdf"));
    }

    /* ============================================================ posters */

    /// <summary>
    /// Uploads or replaces one of the two posters shown on the registration portal.
    /// </summary>
    /// <remarks>
    /// Per competition year, so last year's artwork can go up for testing and be
    /// replaced next August from the same screen. Uploading again over the same slot
    /// replaces what is there — the unique index makes that the only possibility.
    /// </remarks>
    public async Task<Result<bool>> SetAdvertAsync(
        int competitionId, string slot, byte[] content, string fileName, string contentType,
        int? width, int? height, string? actor, CancellationToken ct = default)
    {
        if (slot is not ("Drawing" or "Dance"))
            return Result<bool>.Failure("A poster is either the drawing one or the dance one.");

        if (content.Length == 0)
            return Result<bool>.Failure("That file is empty.");

        if (content.Length > MaxAdvertBytes)
        {
            return Result<bool>.Failure(
                $"That poster is {content.Length / 1024 / 1024} MB. Please use one under 5 MB.");
        }

        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Failure("That file is not an image.");

        var exists = await _db.Competitions.AnyAsync(c => c.Id == competitionId, ct);

        if (!exists)
            return Result<bool>.Failure("That competition no longer exists.", ResultStatus.NotFound);

        var advert = await _db.CompetitionAdverts
            .FirstOrDefaultAsync(a => a.CompetitionId == competitionId && a.Slot == slot, ct);

        if (advert is null)
        {
            advert = new CompetitionAdvert { CompetitionId = competitionId, Slot = slot };
            _db.CompetitionAdverts.Add(advert);
        }

        advert.Content = Convert.ToBase64String(content);
        advert.FileName = fileName;
        advert.ContentType = contentType;
        advert.Width = width;
        advert.Height = height;
        advert.UploadedBy = actor;
        advert.UploadedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Competition {Id}: {Slot} poster uploaded by {Actor} ({Bytes} bytes)",
            competitionId, slot, actor, content.Length);

        return Result<bool>.Success(true);
    }

    public async Task<Result<CompetitionDocumentDto>> GetAdvertAsync(
        int competitionId, string slot, CancellationToken ct = default)
    {
        var advert = await _db.CompetitionAdverts
            .AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && a.Slot == slot)
            .Select(a => new { a.Content, a.ContentType, a.FileName })
            .FirstOrDefaultAsync(ct);

        if (advert is null)
            return Result<CompetitionDocumentDto>.Failure("No poster for that competition.", ResultStatus.NotFound);

        try
        {
            return Result<CompetitionDocumentDto>.Success(new CompetitionDocumentDto(
                Convert.FromBase64String(advert.Content),
                advert.ContentType ?? "image/jpeg",
                advert.FileName ?? $"{slot}-poster.jpg"));
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Competition advert {Slot} for {Id} is not valid Base64.", slot, competitionId);
            return Result<CompetitionDocumentDto>.Failure("That poster could not be read.", ResultStatus.NotFound);
        }
    }

    public async Task<Result<bool>> RemoveAdvertAsync(
        int competitionId, string slot, CancellationToken ct = default)
    {
        var advert = await _db.CompetitionAdverts
            .FirstOrDefaultAsync(a => a.CompetitionId == competitionId && a.Slot == slot, ct);

        if (advert is null)
            return Result<bool>.Failure("There is no poster to remove.", ResultStatus.NotFound);

        _db.CompetitionAdverts.Remove(advert);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<CompetitionDocumentDto>> GetDocumentAsync(
        int applicantId, string kind, CancellationToken ct = default)
    {
        var document = await _db.CompetitionDocuments
            .AsNoTracking()
            .Where(d => d.ApplicantId == applicantId && d.Kind == kind)
            .Select(d => new { d.Content, d.ContentType, d.FileName })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return Result<CompetitionDocumentDto>.Failure("No such document.", ResultStatus.NotFound);

        try
        {
            return Result<CompetitionDocumentDto>.Success(new CompetitionDocumentDto(
                Convert.FromBase64String(document.Content),
                document.ContentType ?? "image/jpeg",
                document.FileName ?? $"{kind}-{applicantId}.jpg"));
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Competition document {Kind} for {Id} is not valid Base64.", kind, applicantId);
            return Result<CompetitionDocumentDto>.Failure("That document could not be read.", ResultStatus.NotFound);
        }
    }

    /* ============================================================ settings */

    public async Task<IReadOnlyList<CompetitionPublicDto>> GetAllAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var competitions = await _db.Competitions
            .AsNoTracking()
            .OrderByDescending(c => c.Year)
            .ToListAsync(ct);

        var ids = competitions.Select(c => c.Id).ToList();

        var groups = await _db.CompetitionGroups
            .AsNoTracking()
            .Where(g => ids.Contains(g.CompetitionId))
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

        var organisers = await _db.CompetitionOrganisers
            .AsNoTracking()
            .Where(o => ids.Contains(o.CompetitionId))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .ToListAsync(ct);

        var adverts = await _db.CompetitionAdverts
            .AsNoTracking()
            .Where(a => ids.Contains(a.CompetitionId))
            .Select(a => new { a.CompetitionId, a.Slot })
            .ToListAsync(ct);

        return competitions
            .Select(c => Describe(
                c,
                groups.Where(g => g.CompetitionId == c.Id).ToList(),
                organisers.Where(o => o.CompetitionId == c.Id).ToList(),
                today,
                adverts.Where(a => a.CompetitionId == c.Id).Select(a => a.Slot).ToList()))
            .ToList();
    }

    public async Task<Result<int>> SaveCompetitionAsync(
        int? id, CompetitionSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var clash = await _db.Competitions
            .AnyAsync(c => c.Year == request.Year && (id == null || c.Id != id), ct);

        if (clash)
            return Result<int>.Failure($"There is already a competition for {request.Year}.", ResultStatus.Conflict);

        Competition competition;

        if (id is > 0)
        {
            var found = await _db.Competitions.FirstOrDefaultAsync(c => c.Id == id, ct);

            if (found is null)
                return Result<int>.Failure("That competition no longer exists.", ResultStatus.NotFound);

            competition = found;
            competition.ModifiedBy = actor;
            competition.ModifiedOn = DateTime.Now;
        }
        else
        {
            competition = new Competition { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.Competitions.Add(competition);
        }

        competition.Year = request.Year;
        competition.EventDate = request.EventDate;
        competition.Venue = Keep(request.Venue, competition.Venue);
        competition.VenueBengali = Keep(request.VenueBengali, competition.VenueBengali);
        competition.DrawingFee = request.DrawingFee;
        competition.DanceFee = request.DanceFee;
        competition.DrawingStartTime = request.DrawingStartTime;
        competition.DanceStartTime = request.DanceStartTime;
        competition.PrizeGivingTime = request.PrizeGivingTime;
        /* The long text is only replaced when something was actually sent.

           Everything else here is a field the screen always fills in, so writing it
           blindly is right. The rules and the notice are different: they are the
           only fields long enough that a caller might omit them, and losing them is
           silent — the portal simply stops showing any rules, which nobody notices
           until a parent asks. That happened twice during testing here, from a save
           that carried the fees and the dates and nothing else.

           The same protection covers the venue and the contact numbers, which are
           text of the same kind: typed once, rarely changed, and gone without a
           trace if a save leaves them out.

           To clear any of them deliberately, save a single space. */
        competition.NoticeEnglish = Keep(request.NoticeEnglish, competition.NoticeEnglish);
        competition.NoticeBengali = Keep(request.NoticeBengali, competition.NoticeBengali);
        competition.DrawingRulesEnglish = Keep(request.DrawingRulesEnglish, competition.DrawingRulesEnglish);
        competition.DrawingRulesBengali = Keep(request.DrawingRulesBengali, competition.DrawingRulesBengali);
        competition.DanceRulesEnglish = Keep(request.DanceRulesEnglish, competition.DanceRulesEnglish);
        competition.DanceRulesBengali = Keep(request.DanceRulesBengali, competition.DanceRulesBengali);
        competition.RegistrationOpensOn = request.RegistrationOpensOn;
        competition.RegistrationClosesOn = request.RegistrationClosesOn;
        competition.ContactPhones = Keep(request.ContactPhones, competition.ContactPhones);
        competition.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(competition.Id);
    }

    public async Task<Result<int>> SaveOrganiserAsync(
        int competitionId, int? organiserId, CompetitionOrganiserSaveRequest request,
        CancellationToken ct = default)
    {
        CompetitionOrganiser organiser;

        if (organiserId is > 0)
        {
            var found = await _db.CompetitionOrganisers
                .FirstOrDefaultAsync(o => o.Id == organiserId, ct);

            if (found is null)
                return Result<int>.Failure("That name is no longer on the form.", ResultStatus.NotFound);

            organiser = found;
        }
        else
        {
            organiser = new CompetitionOrganiser { CompetitionId = competitionId };
            _db.CompetitionOrganisers.Add(organiser);
        }

        organiser.Name = request.Name.Trim();
        organiser.Designation = Blank(request.Designation);
        organiser.Phone = Blank(request.Phone);
        organiser.SortOrder = request.SortOrder;
        organiser.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return Result<int>.Success(organiser.Id);
    }

    public async Task<Result<bool>> DeleteOrganiserAsync(int organiserId, CancellationToken ct = default)
    {
        var organiser = await _db.CompetitionOrganisers
            .FirstOrDefaultAsync(o => o.Id == organiserId, ct);

        if (organiser is null)
            return Result<bool>.Failure("That name is no longer on the form.", ResultStatus.NotFound);

        _db.CompetitionOrganisers.Remove(organiser);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ============================================================= helpers */

    private static Result<CompetitionRegisteredDto> Fail(string message) =>
        Result<CompetitionRegisteredDto>.Failure(message);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The new text, or what is already stored when nothing was sent.
    /// </summary>
    /// <remarks>
    /// Null and empty mean "not supplied" and leave the stored text alone. A string
    /// of whitespace means "clear it" — a deliberate act, and the only way to empty
    /// the rules, which is what makes an accidental omission harmless.
    /// </remarks>
    private static string? Keep(string? incoming, string? stored)
    {
        if (incoming is null || incoming.Length == 0) return stored;
        return string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>Age on a given day, counting the birthday rather than the year alone.</summary>
    private static int AgeOn(DateOnly dateOfBirth, DateOnly on)
    {
        var age = on.Year - dateOfBirth.Year;
        if (on < dateOfBirth.AddYears(age)) age--;
        return age;
    }

    private static readonly string[] RomanClasses =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

    /// <summary>
    /// "Nursery" is 0, "I" through "XII" are 1 to 12 — matching the numbers the
    /// drawing divisions were seeded with (migration 038). Accepts a roman numeral,
    /// a plain digit, or "Class " / "Std " / "Std. " in front of either.
    /// </summary>
    private static int? ParseSchoolClass(string raw)
    {
        var text = raw.Trim();

        foreach (var prefix in new[] { "Class ", "class ", "Std. ", "std. ", "Std ", "std " })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal)) text = text[prefix.Length..].Trim();
        }

        if (text.Equals("Nursery", StringComparison.OrdinalIgnoreCase)
            || text.Equals("LKG", StringComparison.OrdinalIgnoreCase)
            || text.Equals("UKG", StringComparison.OrdinalIgnoreCase)
            || text.Equals("KG", StringComparison.OrdinalIgnoreCase)
            || text == "0")
        {
            return 0;
        }

        if (int.TryParse(text, out var n) && n is >= 1 and <= 12) return n;

        var roman = Array.FindIndex(
            RomanClasses, r => r.Equals(text, StringComparison.OrdinalIgnoreCase));

        return roman >= 0 ? roman + 1 : null;
    }

    private static string SchoolClassLabel(int schoolClass) =>
        schoolClass == 0 ? "Nursery" : $"Class {RomanClasses[schoolClass - 1]}";

    /// <summary>
    /// Turns one uploaded file into a document row, or explains why it cannot.
    /// </summary>
    /// <remarks>
    /// A null upload is not an error here — whether a particular document is required
    /// depends on which competitions were entered, and the caller decides that.
    /// </remarks>
    private static Result<CompetitionDocument?> Decode(CompetitionUploadDto? upload, string kind)
    {
        if (upload is null || string.IsNullOrWhiteSpace(upload.Base64))
            return Result<CompetitionDocument?>.Success(null);

        byte[] bytes;

        try
        {
            // A browser's data URL carries a "data:image/jpeg;base64," preamble.
            var payload = upload.Base64;
            var comma = payload.IndexOf(',');
            if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
                payload = payload[(comma + 1)..];

            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return Result<CompetitionDocument?>.Failure($"The {Describe(kind)} could not be read.");
        }

        if (bytes.Length == 0)
            return Result<CompetitionDocument?>.Failure($"The {Describe(kind)} is empty.");

        if (bytes.Length > MaxDocumentBytes)
        {
            return Result<CompetitionDocument?>.Failure(
                $"The {Describe(kind)} is {bytes.Length / 1024 / 1024} MB. Please use one under 3 MB.");
        }

        return Result<CompetitionDocument?>.Success(new CompetitionDocument
        {
            Kind = kind,
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            Content = Convert.ToBase64String(bytes)
        });
    }

    private static string Describe(string kind) => kind switch
    {
        "Photo" => "photograph",
        "SchoolId" => "school id card",
        "BirthCertificate" => "birth certificate",
        "PaymentSlip" => "payment screenshot",
        _ => "file"
    };
}

/// <summary>
/// Thrown when the last place in a dance group is taken between the check and the
/// insert. Caught by <see cref="CompetitionService.RegisterAsync"/> and turned into
/// a civil message; it never leaves the service.
/// </summary>
public class CompetitionGroupFullException : Exception
{
    public CompetitionGroupFullException(int capacity)
        : base($"The dance group is full at {capacity}.") => Capacity = capacity;

    public int Capacity { get; }
}
