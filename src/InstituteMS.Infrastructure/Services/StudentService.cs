using InstituteMS.Application.Documents;
using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.Students;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Replaces <c>Models/Student.cs</c> + <c>Models/CoName.cs</c> (MIGRATION_PLAN §4.1).
/// The five near-duplicate list queries in the legacy code (active / inactive /
/// by-reg / by-name / with-photo) collapse into one filtered query here (S13, D9).
/// </summary>
public class StudentService : IStudentService
{
    private readonly AppDbContext _db;
    private readonly RegistrationNumberService _numbers;
    private readonly StudentLoginGenerator _logins;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<StudentService> _logger;
    private readonly IStudentMailer _mailer;
    private readonly IReceiptPdfGenerator _pdf;
    private readonly IReceiptLinkService _links;

    public StudentService(
        AppDbContext db,
        RegistrationNumberService numbers,
        StudentLoginGenerator logins,
        IPasswordHasher hasher,
        IStudentMailer mailer,
        IReceiptPdfGenerator pdf,
        IReceiptLinkService links,
        ILogger<StudentService> logger)
    {
        _db = db;
        _numbers = numbers;
        _logins = logins;
        _hasher = hasher;
        _mailer = mailer;
        _pdf = pdf;
        _links = links;
        _logger = logger;
    }

    /* ------------------------------------------------------------------ read */

    public async Task<PagedResult<StudentListItemDto>> GetListAsync(
        StudentQuery query, CancellationToken ct = default)
    {
        var students = Filtered(query);

        var totalCount = await students.CountAsync(ct);

        var page = students
            .OrderByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        var items = await MaterialiseAsync(page, ct);

        return PagedResult<StudentListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<StudentListItemDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var students = await MaterialiseAsync(_db.Students.Where(s => s.Id == id), ct);

        return students.Count == 0
            ? Result<StudentListItemDto>.Failure("Student not found.", ResultStatus.NotFound)
            : Result<StudentListItemDto>.Success(students[0]);
    }

    public async Task<Result<StudentListItemDto>> GetByRegistrationIdAsync(
        string registrationId, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        // Duplicate registration ids exist in the legacy data, so take the newest.
        var students = await MaterialiseAsync(
            _db.Students.Where(s => s.RegistrationId == reg).OrderByDescending(s => s.Id).Take(1), ct);

        return students.Count == 0
            ? Result<StudentListItemDto>.Failure($"No student found with registration id '{reg}'.", ResultStatus.NotFound)
            : Result<StudentListItemDto>.Success(students[0]);
    }

    public async Task<IReadOnlyList<StudentLookupDto>> GetLookupAsync(
        string? search, CancellationToken ct = default)
    {
        var students = _db.Students.AsNoTracking().Where(s => s.IsActive == true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            students = students.Where(s =>
                (s.Name != null && s.Name.Contains(term)) ||
                (s.RegistrationId != null && s.RegistrationId.Contains(term)));
        }

        return await students
            .OrderBy(s => s.Name)
            .Take(50)
            .Select(s => new StudentLookupDto(
                s.Id,
                s.Name,
                s.RegistrationId,
                s.Phone,
                s.Course != null ? s.Course.Name : null))
            .ToListAsync(ct);
    }

    public async Task<Result<AdmissionReceiptDto>> GetReceiptAsync(int id, CancellationToken ct = default)
    {
        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.RegistrationId,
                s.BillNo,
                CourseName = s.Course != null ? s.Course.Name : null,
                s.AdmissionFees,
                s.DateOfJoining
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<AdmissionReceiptDto>.Failure("Student not found.", ResultStatus.NotFound);

        return Result<AdmissionReceiptDto>.Success(new AdmissionReceiptDto(
            student.Id,
            student.Name,
            student.RegistrationId,
            student.BillNo,
            student.CourseName,
            student.AdmissionFees,
            NumberToWords.Convert(student.AdmissionFees ?? 0m),
            student.DateOfJoining,
            // Legacy convert3(): joining date + 32 days.
            student.DateOfJoining?.AddDays(32),
            _links.CreateUrl(ReceiptKind.Admission, student.Id)));
    }

    public async Task<NextRegistrationDto> PeekNextRegistrationAsync(
        int? branchId, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(branchId, ct);
        return await _numbers.PeekAsync(branch?.Code, ct);
    }

    public async Task<SuggestedLoginDto> SuggestLoginAsync(
        string? studentName, int? excludingStudentId = null, CancellationToken ct = default)
    {
        var (email, password) = await _logins.GenerateAsync(studentName, excludingStudentId, ct);
        return new SuggestedLoginDto(email, password);
    }

    public async Task<Result<(byte[] Content, string ContentType, string FileName)>> GetPhotoAsync(
        string registrationId, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var photo = await _db.StudentPhotos
            .AsNoTracking()
            .Where(p => p.StudentRegistrationId == reg)
            .OrderByDescending(p => p.FileId)
            .Select(p => new { p.FileName, p.FilePath, p.ContentType, p.IsInline })
            .FirstOrDefaultAsync(ct);

        if (photo?.FilePath is null)
            return Result<(byte[], string, string)>.Failure("No photo on file.", ResultStatus.NotFound);

        // IsInline is set by db/migrations/006. Rows with IsInline = 0 came from the
        // old disk-based uploader: the database holds only a path, and the bytes are
        // still on the legacy web server.
        if (!photo.IsInline)
        {
            _logger.LogInformation(
                "Photo for {RegistrationId} is a legacy disk path ({Path}); the file is not in the database.",
                reg, photo.FilePath);

            return Result<(byte[], string, string)>.Failure(
                "This photo was stored on the old web server's disk and was not part of the database migration.",
                ResultStatus.NotFound);
        }

        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(photo.FilePath);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Photo for {RegistrationId} is not valid Base64.", reg);
            return Result<(byte[], string, string)>.Failure("Stored photo is corrupt.", ResultStatus.NotFound);
        }

        var name = photo.FileName ?? $"{reg.Replace('/', '-')}.jpg";
        return Result<(byte[], string, string)>.Success(
            (bytes, photo.ContentType ?? GuessContentType(name), name));
    }

    /* ---------------------------------------------------------------- photos */

    /// <summary>
    /// Matches the legacy client-side rules in <c>Scripts/ImageFile.js</c>: PNG, JPEG
    /// or GIF, at most 512 KB. Those ran only in the browser; they run here now.
    /// </summary>
    private const int MaxPhotoBytes = 512 * 1024;

    private static readonly Dictionary<string, string> AllowedPhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif"
    };

    public async Task<Result<StudentPhotoDto>> UploadPhotoAsync(
        string registrationId, PhotoUpload upload, string? actor, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        if (!await _db.Students.AnyAsync(s => s.RegistrationId == reg, ct))
            return Result<StudentPhotoDto>.Failure($"No student found with registration id '{reg}'.", ResultStatus.NotFound);

        if (upload.Length <= 0)
            return Result<StudentPhotoDto>.Failure("The selected file is empty.", ResultStatus.BadRequest);

        if (upload.Length > MaxPhotoBytes)
        {
            return Result<StudentPhotoDto>.Failure(
                $"The image must be {MaxPhotoBytes / 1024} KB or smaller (this one is {upload.Length / 1024} KB).",
                ResultStatus.BadRequest);
        }

        var extension = Path.GetExtension(upload.FileName);
        if (!AllowedPhotoTypes.TryGetValue(extension, out var expectedContentType))
        {
            return Result<StudentPhotoDto>.Failure(
                "Only PNG, JPEG and GIF images are allowed.", ResultStatus.BadRequest);
        }

        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        // The extension and the browser-supplied content type are both attacker
        // controlled, so the bytes themselves decide (defect D10).
        var sniffed = SniffImageType(bytes);
        if (sniffed is null)
            return Result<StudentPhotoDto>.Failure("That file is not a valid PNG, JPEG or GIF image.", ResultStatus.BadRequest);

        if (!string.Equals(sniffed, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Result<StudentPhotoDto>.Failure(
                $"The file contents ({sniffed}) do not match its '{extension}' extension.",
                ResultStatus.BadRequest);
        }

        // One photo per student: the legacy list joined on registration id and would
        // show whichever row came back first, so replace rather than accumulate.
        var existing = await _db.StudentPhotos.Where(p => p.StudentRegistrationId == reg).ToListAsync(ct);
        if (existing.Count > 0) _db.StudentPhotos.RemoveRange(existing);

        var photo = new StudentPhoto
        {
            FileName = Path.GetFileName(upload.FileName),
            Description = reg,           // legacy join key, kept in step
            StudentRegistrationId = reg,
            FilePath = System.Convert.ToBase64String(bytes),
            FileSize = bytes.Length,
            ContentType = sniffed,
            IsInline = true,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.StudentPhotos.Add(photo);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Photo {FileId} ({Bytes} bytes) stored for {RegistrationId} by {Actor}",
            photo.FileId, bytes.Length, reg, actor);

        return Result<StudentPhotoDto>.Success(
            new StudentPhotoDto(photo.FileId, photo.FileName!, sniffed, bytes.Length));
    }

    public async Task<Result<bool>> DeletePhotoAsync(string registrationId, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var photos = await _db.StudentPhotos.Where(p => p.StudentRegistrationId == reg).ToListAsync(ct);
        if (photos.Count == 0)
            return Result<bool>.Failure("No photo on file.", ResultStatus.NotFound);

        _db.StudentPhotos.RemoveRange(photos);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ======================================== admission fee, part payment */

    public async Task<Result<AdmissionDueDto>> GetAdmissionDueAsync(
        int studentId, CancellationToken ct = default)
    {
        var student = await _db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == studentId, ct);

        if (student is null)
            return Result<AdmissionDueDto>.Failure("Student not found.", ResultStatus.NotFound);

        return Result<AdmissionDueDto>.Success(await BuildAdmissionDueAsync(student, ct));
    }

    public async Task<Result<AdmissionDueDto>> CollectAdmissionDueAsync(
        int studentId, AdmissionDueCollectRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.Amount <= 0m)
            return Result<AdmissionDueDto>.Failure("Enter an amount greater than zero.");

        if (request.PaymentMode is not ("Cash" or "Bank"))
            return Result<AdmissionDueDto>.Failure("Payment mode must be Cash or Bank.");

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);

        if (student is null)
            return Result<AdmissionDueDto>.Failure("Student not found.", ResultStatus.NotFound);

        var fee = student.AdmissionFees ?? 0m;

        // Null on a student admitted before migration 051 means paid in full, the
        // same reading the backfill took — not zero received.
        var paid = student.AdmissionFeesPaid ?? fee;
        var due = fee - paid;

        if (due <= 0m)
            return Result<AdmissionDueDto>.Failure("This admission fee is already settled.");

        /* Refused rather than silently capped. Taking 400 against a 300 balance is a
           mistyped figure or the wrong student, and quietly booking 300 of it would
           leave the counter's cash drawer disagreeing with the receipt. */
        if (request.Amount > due)
        {
            return Result<AdmissionDueDto>.Failure(
                $"That is more than the outstanding admission fee of {due:0.##}.");
        }

        _db.AdmissionFeePayments.Add(new AdmissionFeePayment
        {
            StudentId = student.Id,
            RegistrationId = student.RegistrationId,
            Amount = request.Amount,
            PaymentMode = request.PaymentMode,
            PaidOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Today),
            IsAtAdmission = false,
            Note = request.Note?.Trim(),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        });

        // The receipt and the running total move together or not at all.
        student.AdmissionFeesPaid = paid + request.Amount;
        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Admission due {Amount} collected for student {StudentId} ({RegistrationId}) by {Actor}",
            request.Amount, student.Id, student.RegistrationId, actor ?? "(unknown)");

        return Result<AdmissionDueDto>.Success(await BuildAdmissionDueAsync(student, ct));
    }

    /// <summary>
    /// Applies an edit to "admission fee received" on an existing student — a
    /// correction of what was recorded, not new money. New money goes through
    /// <see cref="CollectAdmissionDueAsync"/>.
    /// </summary>
    /// <returns>An error message, or null when the edit was applied (or there was none).</returns>
    /// <remarks>
    /// <para>The case this exists for: 250 was typed at admission when 750 was in fact
    /// taken. The office corrects the fee to 750 and the received figure to 750, and
    /// nothing should be left reading as owing. Leaving the figure read-only made
    /// that impossible without a database edit.</para>
    ///
    /// <para>The receipt ledger has to move with it or the two disagree. The
    /// difference is applied to the receipt written at admission — that is the
    /// receipt being corrected, since later instalments were each taken deliberately.
    /// A student admitted before receipts existed has none to correct, and only the
    /// running total moves, which is the same stance the backfill took.</para>
    /// </remarks>
    private async Task<string?> CorrectAdmissionReceivedAsync(
        Student student, StudentSaveRequest request, string? actor, CancellationToken ct)
    {
        var atAdmission = await _db.AdmissionFeePayments
            .Where(p => p.StudentId == student.Id && p.IsAtAdmission)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

        /* The payment-mode radio on the form is the mode of the admission-day money.
           Apply() has already moved Student.AdmissionPaymentMode; the receipt for
           that day has to follow, or the collections report would count a
           corrected Cash-to-Bank admission under the old mode. Independent of
           whether the amount changed: switching the mode alone is a real correction. */
        if (atAdmission is not null
            && !string.IsNullOrWhiteSpace(request.AdmissionPaymentMode)
            && atAdmission.PaymentMode != request.AdmissionPaymentMode)
        {
            atAdmission.PaymentMode = request.AdmissionPaymentMode;
        }

        // Null on an update means "not touched", so the ordinary edit of a name or a
        // phone number never reaches into the amount.
        if (request.AdmissionFeesPaid is not { } newPaid)
            return null;

        var fee = student.AdmissionFees ?? 0m;
        var oldPaid = student.AdmissionFeesPaid ?? fee;

        if (newPaid == oldPaid)
            return null;

        if (newPaid < 0m)
            return "Amount received cannot be negative.";

        if (newPaid > fee)
            return "Amount received cannot be more than the admission fee.";

        var delta = newPaid - oldPaid;

        if (atAdmission is not null)
        {
            var corrected = atAdmission.Amount + delta;

            /* The correction is to the admission receipt alone; it cannot be pushed
               below zero by pretending money taken later was never taken. Anything
               that large is not a typo being fixed. */
            if (corrected < 0m)
            {
                return "That would take the admission-day receipt below zero. " +
                       "Reduce or remove the later instalment instead.";
            }

            var original = atAdmission.Amount;
            atAdmission.Amount = corrected;

            // Leaves a trace on the receipt itself, so a later reader can see the
            // figure was corrected rather than wonder why it differs from memory.
            if (string.IsNullOrWhiteSpace(atAdmission.Note))
                atAdmission.Note = $"Corrected from {original:0.##} by {actor ?? "(unknown)"}";
        }

        student.AdmissionFeesPaid = newPaid;

        _logger.LogInformation(
            "Admission received corrected {Old} -> {New} for student {StudentId} by {Actor}",
            oldPaid, newPaid, student.Id, actor ?? "(unknown)");

        return null;
    }

    private async Task<AdmissionDueDto> BuildAdmissionDueAsync(Student student, CancellationToken ct)
    {
        var receipts = await _db.AdmissionFeePayments.AsNoTracking()
            .Where(p => p.StudentId == student.Id)
            .OrderBy(p => p.PaidOn)
            .ThenBy(p => p.Id)
            .Select(p => new AdmissionFeeReceiptDto(
                p.Id, p.Amount, p.PaymentMode, p.PaidOn, p.IsAtAdmission, p.Note, p.CreatedBy))
            .ToListAsync(ct);

        var fee = student.AdmissionFees ?? 0m;
        var paid = student.AdmissionFeesPaid ?? fee;
        var due = fee - paid;

        return new AdmissionDueDto(
            student.Id,
            student.RegistrationId,
            student.Name,
            fee,
            paid,
            due > 0m ? due : 0m,
            receipts);
    }

    /// <summary>Identifies an image from its magic bytes, ignoring the file name.</summary>
    private static string? SniffImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
            (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
            return "image/gif";

        return null;
    }

    /* ----------------------------------------------------------------- write */

    public async Task<Result<StudentCreatedDto>> CreateAsync(
        StudentSaveRequest request, string? actor, CancellationToken ct = default)
    {
        /* A new admission must name its branch. Enforced here rather than in the
           validator because the validator also serves the update path, where a
           student admitted before this rule is allowed to keep the branch already
           recorded against them instead of being blocked at every edit. */
        if (request.BranchId is not > 0)
            return Result<StudentCreatedDto>.Failure("Please select a branch.", ResultStatus.BadRequest);

        var referenceError = await ValidateReferencesAsync(request, ct);
        if (referenceError is not null)
            return Result<StudentCreatedDto>.Failure(referenceError, ResultStatus.BadRequest);

        // EnableRetryOnFailure is on for the context, so a user-initiated transaction
        // has to run inside the execution strategy — otherwise the whole unit cannot
        // be replayed safely after a transient fault.
        var strategy = _db.Database.CreateExecutionStrategy();

        // The branch decides the shape of the registration id (AVI/Raj/3463/26).
        var branch = await ResolveBranchAsync(request.BranchId, ct);

        var student = await strategy.ExecuteAsync(async () =>
        {
            // The number reservation and the insert share one transaction, so no other
            // admission can claim the same registration id in between (defect D4).
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var numbers = await _numbers.ReserveAsync(branch?.Code, ct);

            var entity = new Student
            {
                RegistrationId = numbers.RegistrationId,
                BillNo = numbers.BillNo,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
                LoginId = actor
            };

            Apply(request, entity);
            entity.Branch = branch?.Name;

            /* Null means "paying the whole thing", which is what every admission did
               before part payment existed — so a client that does not send the field
               still books a fully-paid admission rather than quietly opening a due. */
            var paidNow = request.AdmissionFeesPaid ?? request.AdmissionFees ?? 0m;
            entity.AdmissionFeesPaid = paidNow;

            // The legacy usp_StudentSaveData set DOE only when IsComplete was ticked.
            entity.DateOfExit = request.IsComplete ? DateOnly.FromDateTime(DateTime.Today) : null;

            _db.Students.Add(entity);
            await _db.SaveChangesAsync(ct);

            /* The receipt for whatever was actually handed over at the counter. Only
               when there was something: an admission taken entirely on credit is a
               real case, and a zero-rupee receipt would just be noise in the ledger. */
            if (paidNow > 0m)
            {
                _db.AdmissionFeePayments.Add(new AdmissionFeePayment
                {
                    StudentId = entity.Id,
                    RegistrationId = entity.RegistrationId,
                    Amount = paidNow,
                    PaymentMode = request.AdmissionPaymentMode,
                    PaidOn = request.DateOfJoining ?? DateOnly.FromDateTime(DateTime.Today),
                    IsAtAdmission = true,
                    CreatedBy = actor,
                    CreatedOn = DateTime.Now
                });
            }

            // Legacy Replica_id is the row's own id.
            entity.ReplicaId = entity.Id;

            // The portal sign-in, and the account the student actually uses. The
            // legacy usp_StudentSaveData did both in the same procedure, so a
            // student could sign in the moment they were registered; the generated
            // values were passed in from the browser. They are generated here
            // instead, and only when the caller did not supply them.
            await IssuePortalLoginAsync(entity, request, actor, ct);

            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return entity;
        });

        _logger.LogInformation(
            "Student {StudentId} admitted as {RegistrationId} by {Actor}",
            student.Id, student.RegistrationId, actor);

        /* Sent after the transaction has committed, so a slow or unreachable mail
           server cannot hold a database transaction open - and deliberately
           awaited rather than fired and forgotten, so the attempt is logged
           before the response returns. The mailer never throws. */
        var courseName = student.CourseId is null
            ? null
            : await _db.Courses.AsNoTracking()
                .Where(c => c.Cid == student.CourseId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);

        /* The admission receipt as a PDF, attached to the message. Built from the
           same GetReceiptAsync the printed one uses, so the two cannot disagree.
           An addition to the mail, never a condition of it: if it fails to render
           the student still gets the message. */
        byte[]? pdf = null;
        var receiptResult = await GetReceiptAsync(student.Id, ct);

        if (receiptResult.Succeeded && receiptResult.Value is not null)
        {
            try { pdf = _pdf.AdmissionReceipt(receiptResult.Value); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not render the PDF for admission {RegistrationId}; sending without it.",
                    student.RegistrationId);
            }
        }

        await _mailer.SendWelcomeAsync(
            new RegistrationMailRequest(
                student.RegistrationId!,
                student.Name ?? string.Empty,
                courseName,
                student.PersonalEmail,
                student.AvijanLoginEmail,
                student.AvijanLoginPassword,
                /* The admission receipt, so the emailed copy carries the same
                   figures as the printed one. Worked out the same way
                   GetReceiptAsync does, including the legacy convert3() rule
                   that the next payment falls 32 days after joining. */
                student.BillNo,
                student.AdmissionFees,
                NumberToWords.Convert(student.AdmissionFees ?? 0m),
                student.DateOfJoining,
                student.DateOfJoining?.AddDays(32),
                pdf,
                $"Avijan-Admission-Receipt-{(student.BillNo ?? "receipt").Replace('/', '-')}.pdf"),
            actor, ct);

        return Result<StudentCreatedDto>.Success(
            new StudentCreatedDto(student.Id, student.RegistrationId!, student.BillNo!));
    }

    /// <summary>
    /// Gives a newly registered student their Avijan portal sign-in and the
    /// matching <c>dbo.Users</c> account, the way <c>usp_StudentSaveData</c> did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two copies of the password are written, deliberately and differently:
    /// <c>Student.Avijan_login_Password</c> keeps it in clear so the office can read
    /// it back to the student, exactly as production stores it, while
    /// <c>Users.PasswordHash</c> holds the BCrypt hash that sign-in actually checks.
    /// Changing it later through the Users screen updates both.
    /// </para>
    /// <para>
    /// If an account already exists on that address it is left alone rather than
    /// overwritten — the legacy insert would have created a second row on the same
    /// email and left sign-in to pick one.
    /// </para>
    /// </remarks>
    private async Task IssuePortalLoginAsync(
        Student entity, StudentSaveRequest request, string? actor, CancellationToken ct)
    {
        var email = request.AvijanLoginEmail?.Trim();
        var password = request.AvijanLoginPassword?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            var generated = await _logins.GenerateAsync(entity.Name, entity.Id, ct);

            email = string.IsNullOrWhiteSpace(email) ? generated.Email : email;
            password = string.IsNullOrWhiteSpace(password) ? generated.Password : password;
        }

        entity.AvijanLoginEmail = email;
        entity.AvijanLoginPassword = password;

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return;

        _db.Users.Add(new User
        {
            Email = email,
            Roles = UserRoles.Student,
            PasswordHash = _hasher.Hash(password!),
            IsActive = entity.IsActive == true,
            LoginId = actor,
            RegisterDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = entity.DateOfExit,
            Name = entity.Name,
            PhoneNumber = entity.Phone,
            DateOfBirth = entity.DateOfBirth,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        });
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, StudentSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
            return Result<bool>.Failure("Student not found.", ResultStatus.NotFound);

        var referenceError = await ValidateReferencesAsync(request, ct);
        if (referenceError is not null)
            return Result<bool>.Failure(referenceError, ResultStatus.BadRequest);

        var wasComplete = student.IsComplete == true;

        Apply(request, student);

        var correction = await CorrectAdmissionReceivedAsync(student, request, actor, ct);
        if (correction is not null)
            return Result<bool>.Failure(correction);

        // The registration id is immutable, so changing branch on an existing student
        // updates only the stored branch name, never the already-issued number.
        var branch = await ResolveBranchAsync(request.BranchId, ct);
        if (branch is not null) student.Branch = branch.Name;

        // Stamp the exit date on the transition into "complete", and clear it if
        // the student is reopened.
        if (request.IsComplete && !wasComplete)
            student.DateOfExit = DateOnly.FromDateTime(DateTime.Today);
        else if (!request.IsComplete)
            student.DateOfExit = null;

        var loginError = await UpdatePortalLoginAsync(student, request, actor, ct);
        if (loginError is not null)
            return Result<bool>.Failure(loginError);

        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.UtcNow;
        student.LoginId = actor;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Student {StudentId} updated by {Actor}", id, actor);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Applies a sign-in regenerated (or manually typed) on the edit screen to the
    /// student's own record and to the matching <c>Users</c> row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only acts when the request actually carries a password: an ordinary save,
    /// with the login fields untouched, sends both blank, and this must not reset a
    /// student's working sign-in every time an unrelated field changes.
    /// </para>
    /// <para>
    /// Before this, clicking Regenerate on an existing student's edit screen and
    /// saving looked like it worked — the new email and password sat in the form —
    /// but neither <c>Student.AvijanLoginPassword</c> nor <c>Users.PasswordHash</c>
    /// were ever touched, so the student's actual sign-in never changed. This
    /// writes both, the same dual-write <see cref="IssuePortalLoginAsync"/> does on
    /// admission and <c>UserService.SetPasswordAsync</c> does from the Users
    /// screen, and ends the account's other sessions the same way a password
    /// change always does.
    /// </para>
    /// </remarks>
    private async Task<string?> UpdatePortalLoginAsync(
        Student student, StudentSaveRequest request, string? actor, CancellationToken ct)
    {
        var newPassword = request.AvijanLoginPassword?.Trim();
        if (string.IsNullOrWhiteSpace(newPassword))
            return null;

        var oldEmail = student.AvijanLoginEmail;
        var newEmail = request.AvijanLoginEmail?.Trim();
        if (string.IsNullOrWhiteSpace(newEmail)) newEmail = oldEmail;

        var user = string.IsNullOrWhiteSpace(oldEmail)
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.Email == oldEmail, ct);

        // The address is changing to one already in use by a different account —
        // refuse rather than silently pointing two students at one login.
        if (!string.Equals(newEmail, oldEmail, StringComparison.OrdinalIgnoreCase)
            && await _db.Users.AnyAsync(u => u.Email == newEmail && u != user, ct))
        {
            return $"'{newEmail}' is already used by another account.";
        }

        student.AvijanLoginEmail = newEmail;
        student.AvijanLoginPassword = newPassword;

        if (user is null)
        {
            // No account yet — a legacy student who never had one, for instance.
            // Issue one now, the same way admission does.
            _db.Users.Add(new User
            {
                Email = newEmail!,
                Roles = UserRoles.Student,
                PasswordHash = _hasher.Hash(newPassword),
                IsActive = student.IsActive == true,
                LoginId = actor,
                RegisterDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = student.DateOfExit,
                Name = student.Name,
                PhoneNumber = student.Phone,
                DateOfBirth = student.DateOfBirth,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            });

            return null;
        }

        user.Email = newEmail!;
        user.PasswordHash = _hasher.Hash(newPassword);
        user.ModifiedBy = actor;
        user.ModifiedOn = DateTime.UtcNow;

        // Any password change ends that user's other sessions — the same rule
        // UserService.SetPasswordAsync applies from the Users screen.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        return null;
    }

    public async Task<Result<bool>> DeactivateAsync(int id, string? actor, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
            return Result<bool>.Failure("Student not found.", ResultStatus.NotFound);

        if (!student.IsActive == true)
            return Result<bool>.Failure("Student is already inactive.", ResultStatus.Conflict);

        // Soft delete. The legacy "Delete Student where id=..." destroyed the row and
        // orphaned its fee ledger, marks and photo (defect S5).
        student.IsActive = false;
        student.DateOfExit = DateOnly.FromDateTime(DateTime.Today);
        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.UtcNow;

        /* The portal sign-in (dbo.Users, matched on AvijanLoginEmail -- the only
           reliable link, since LoginId carries a placeholder value on thousands of
           legacy rows) used to mirror Student.IsActive only once, at the moment the
           login was first issued. Deactivating a student months or years later left
           that sign-in untouched, so a student who left could still sign in and use
           the portal indefinitely. The owner's rule: "Only active account will be
           there, inactive account will not take any active action." */
        if (!string.IsNullOrWhiteSpace(student.AvijanLoginEmail))
        {
            var login = await _db.Users.FirstOrDefaultAsync(u => u.Email == student.AvijanLoginEmail, ct);
            if (login is not null) login.IsActive = false;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Student {StudentId} deactivated by {Actor}", id, actor);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> ActivateAsync(
        string registrationId, string? actor, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var students = await _db.Students.Where(s => s.RegistrationId == reg).ToListAsync(ct);
        if (students.Count == 0)
            return Result<int>.Failure($"No student found with registration id '{reg}'.", ResultStatus.NotFound);

        foreach (var student in students)
        {
            student.IsActive = true;
            student.DateOfExit = null;
            student.ModifiedBy = actor;
            student.ModifiedOn = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(student.AvijanLoginEmail))
            {
                var login = await _db.Users.FirstOrDefaultAsync(u => u.Email == student.AvijanLoginEmail, ct);
                if (login is not null) login.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reactivated {Count} student row(s) on {RegistrationId} by {Actor}",
            students.Count, reg, actor);

        return Result<int>.Success(students.Count);
    }

    /* --------------------------------------------------------------- helpers */

    private IQueryable<Student> Filtered(StudentQuery query)
    {
        var students = _db.Students.AsNoTracking().AsQueryable();

        if (query.IsActive is { } isActive)
            students = students.Where(s => s.IsActive == isActive);

        if (query.CourseId is { } courseId)
            students = students.Where(s => s.CourseId == courseId);

        if (query.TeacherId is { } teacherId)
            students = students.Where(s =>
                s.PrimaryTeacherId == teacherId ||
                s.SecondTeacherId == teacherId ||
                s.ThirdTeacherId == teacherId);

        if (!string.IsNullOrWhiteSpace(query.RegistrationId))
        {
            var reg = query.RegistrationId.Trim();
            students = students.Where(s => s.RegistrationId != null && s.RegistrationId.Contains(reg));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            students = students.Where(s => s.Name != null && s.Name.Contains(name));
        }

        // Free-text box: matches name, registration id, phone or email.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            students = students.Where(s =>
                (s.Name != null && s.Name.Contains(term)) ||
                (s.RegistrationId != null && s.RegistrationId.Contains(term)) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.Email != null && s.Email.Contains(term)));
        }

        return students;
    }

    /// <summary>
    /// Runs the projection, then fills in <c>HasPhoto</c> with a second query.
    /// EF Core cannot translate a correlated EXISTS inside this projection because
    /// of the three teacher left-joins already in it, and pulling the flag in a
    /// separate round trip is cheaper than the alternative anyway.
    /// </summary>
    private async Task<List<StudentListItemDto>> MaterialiseAsync(
        IQueryable<Student> students, CancellationToken ct)
    {
        var rows = await Project(students).ToListAsync(ct);
        if (rows.Count == 0) return rows;

        // Student.Branch stores the branch *name* (legacy shape); the form needs the
        // id. There are only a couple of branches, so map them in memory.
        var branchesByName = await _db.Branches
            .AsNoTracking()
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Name ?? string.Empty, b => b.Id, StringComparer.OrdinalIgnoreCase, ct);

        rows = rows
            .Select(r => r.Branch is not null && branchesByName.TryGetValue(r.Branch, out var branchId)
                ? r with { BranchId = branchId }
                : r)
            .ToList();

        var registrationIds = rows
            .Select(r => r.RegistrationId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        if (registrationIds.Count == 0) return rows;

        // Only count photos we can actually serve; a legacy disk-path row would
        // render as a broken image.
        var withPhoto = (await _db.StudentPhotos
                .AsNoTracking()
                .Where(p => p.StudentRegistrationId != null
                            && registrationIds.Contains(p.StudentRegistrationId)
                            && p.IsInline
                            && p.FilePath != null)
                .Select(p => p.StudentRegistrationId!)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        return rows
            .Select(r => r.RegistrationId is not null && withPhoto.Contains(r.RegistrationId)
                ? r with { HasPhoto = true }
                : r)
            .ToList();
    }

    private static IQueryable<StudentListItemDto> Project(IQueryable<Student> students) =>
        students.AsNoTracking().Select(s => new StudentListItemDto(
            s.Id,
            s.Name,
            s.GuardianName,
            s.RegistrationId,
            s.BillNo,
            s.Phone,
            s.Email,
            s.Address,
            s.CourseId,
            s.Course != null ? s.Course.Name : null,
            s.Course != null ? s.Course.Duration : null,
            s.TechniqueCount,
            s.PrimaryTeacherId,
            s.PrimaryTeacher != null ? s.PrimaryTeacher.Name : null,
            s.SecondTeacherId,
            s.SecondTeacher != null ? s.SecondTeacher.Name : null,
            s.ThirdTeacherId,
            s.ThirdTeacher != null ? s.ThirdTeacher.Name : null,
            s.AdmissionFees,
            s.AdmissionPaymentMode,
            s.MonthlyFees,
            s.DateOfJoining,
            s.DateOfBirth,
            s.DateOfExit,
            s.IsActive == true,
            s.IsComplete == true,
            null,      // BranchId  } both filled in by MaterialiseAsync
            s.Branch,
            false,     // HasPhoto  }
            s.AdmissionFeesPaid,
            /* Floored at zero: a fee later reduced below what was already taken
               reads as settled, not as the institute owing the student money. */
            (s.AdmissionFees ?? 0m) - (s.AdmissionFeesPaid ?? s.AdmissionFees ?? 0m) > 0m
                ? (s.AdmissionFees ?? 0m) - (s.AdmissionFeesPaid ?? s.AdmissionFees ?? 0m)
                : 0m,
            s.PersonalEmail));

    /// <summary>Copies the request onto the entity. Shared by create and update.</summary>
    private static void Apply(StudentSaveRequest request, Student student)
    {
        student.Name = request.Name?.Trim();
        student.GuardianName = request.GuardianName?.Trim();
        student.Address = request.Address?.Trim();
        student.Phone = request.Phone?.Trim();
        student.Email = request.Email?.Trim();
        /* Null means the field was not sent, not that it should be cleared. This is
           where receipts and password-reset codes go, and it was being erased on
           every edit by a form that never loaded it and so posted it back empty.
           Required on create, so null cannot reach here from a new admission; on an
           update it keeps what is there. Clearing deliberately is an empty string,
           stored as null to match the 932 legacy rows that never had one. */
        if (request.PersonalEmail is not null)
        {
            var personal = request.PersonalEmail.Trim();
            student.PersonalEmail = personal.Length == 0 ? null : personal;
        }
        student.DateOfBirth = request.DateOfBirth;
        student.DateOfJoining = request.DateOfJoining;
        student.MonthlyFees = request.MonthlyFees;
        student.AdmissionFees = request.AdmissionFees;
        student.AdmissionPaymentMode = request.AdmissionPaymentMode;
        student.CourseId = request.CourseId;
        student.TechniqueCount = request.TeacherCount;
        student.IsActive = request.IsActive;
        student.IsComplete = request.IsComplete;
        // Branch is set by the caller from the resolved Branch row, not from the
        // request, so the stored name always matches a real branch.

        // Only keep the teacher slots the chosen count actually uses — the legacy
        // form cleared the hidden dropdowns client-side and we must not leave a
        // stale id behind when the count is reduced.
        var count = request.TeacherCount ?? 1;
        student.PrimaryTeacherId = Normalise(request.PrimaryTeacherId);
        student.SecondTeacherId = count >= 2 ? Normalise(request.SecondTeacherId) : null;
        student.ThirdTeacherId = count >= 3 ? Normalise(request.ThirdTeacherId) : null;
    }

    /// <summary>0 was the legacy "no teacher" sentinel; the FK needs NULL.</summary>
    private static int? Normalise(int? teacherId) => teacherId is > 0 ? teacherId : null;

    /// <summary>
    /// Looks up the chosen branch so the registration id can carry its code and the
    /// student can store its name. Returns null when no branch was selected, which
    /// yields the original branch-less registration format - reachable now only for
    /// records written before branch became mandatory, since ValidateReferencesAsync
    /// rejects both a missing branch and one that does not resolve.
    /// </summary>
    private async Task<Branch?> ResolveBranchAsync(int? branchId, CancellationToken ct)
    {
        if (branchId is not > 0) return null;

        return await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct);
    }

    /// <summary>
    /// The legacy uploader stored the Base64 payload but never the MIME type, so it
    /// has to be inferred from the stored file name.
    /// </summary>
    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "image/jpeg"
        };

    /// <summary>Checks the FK targets exist so the user gets a message, not a 500.</summary>
    private async Task<string?> ValidateReferencesAsync(StudentSaveRequest request, CancellationToken ct)
    {
        if (request.CourseId is { } courseId &&
            !await _db.Courses.AnyAsync(c => c.Cid == courseId, ct))
        {
            return "The selected course no longer exists.";
        }

        var teacherIds = new[] { request.PrimaryTeacherId, request.SecondTeacherId, request.ThirdTeacherId }
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (teacherIds.Count > 0)
        {
            var found = await _db.Teachers.CountAsync(t => teacherIds.Contains(t.Id), ct);
            if (found != teacherIds.Count)
                return "One of the selected teachers no longer exists.";
        }

        /* Branch has to resolve, not merely be present. ResolveBranchAsync answers
           null both for "none chosen" and for "chosen but no such row", and the
           callers treat null as "no branch" - so without this check an id that does
           not exist is accepted and quietly saves a student with no branch at all,
           which is exactly what making the field required is meant to prevent. */
        if (request.BranchId is { } branchId &&
            !await _db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            return "The selected branch no longer exists.";
        }

        return null;
    }
}
