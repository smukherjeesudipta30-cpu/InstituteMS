using InstituteMS.Application.Common;
using InstituteMS.Application.Students;
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
    private readonly ILogger<StudentService> _logger;

    public StudentService(
        AppDbContext db,
        RegistrationNumberService numbers,
        ILogger<StudentService> logger)
    {
        _db = db;
        _numbers = numbers;
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
        var students = _db.Students.AsNoTracking().Where(s => s.IsActive);

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
            student.DateOfJoining?.AddDays(32)));
    }

    public async Task<NextRegistrationDto> PeekNextRegistrationAsync(
        int? branchId, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(branchId, ct);
        return await _numbers.PeekAsync(branch?.Code, ct);
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

            // The legacy usp_StudentSaveData set DOE only when IsComplete was ticked.
            entity.DateOfExit = request.IsComplete ? DateOnly.FromDateTime(DateTime.Today) : null;

            _db.Students.Add(entity);
            await _db.SaveChangesAsync(ct);

            // Legacy Replica_id is the row's own id.
            entity.ReplicaId = entity.Id;
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return entity;
        });

        _logger.LogInformation(
            "Student {StudentId} admitted as {RegistrationId} by {Actor}",
            student.Id, student.RegistrationId, actor);

        return Result<StudentCreatedDto>.Success(
            new StudentCreatedDto(student.Id, student.RegistrationId!, student.BillNo!));
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

        var wasComplete = student.IsComplete;

        Apply(request, student);

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

        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.UtcNow;
        student.LoginId = actor;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Student {StudentId} updated by {Actor}", id, actor);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeactivateAsync(int id, string? actor, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
            return Result<bool>.Failure("Student not found.", ResultStatus.NotFound);

        if (!student.IsActive)
            return Result<bool>.Failure("Student is already inactive.", ResultStatus.Conflict);

        // Soft delete. The legacy "Delete Student where id=..." destroyed the row and
        // orphaned its fee ledger, marks and photo (defect S5).
        student.IsActive = false;
        student.DateOfExit = DateOnly.FromDateTime(DateTime.Today);
        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.UtcNow;

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
            s.MonthlyFees,
            s.DateOfJoining,
            s.DateOfBirth,
            s.DateOfExit,
            s.IsActive,
            s.IsComplete,
            null,      // BranchId  } both filled in by MaterialiseAsync
            s.Branch,
            false));   // HasPhoto  }

    /// <summary>Copies the request onto the entity. Shared by create and update.</summary>
    private static void Apply(StudentSaveRequest request, Student student)
    {
        student.Name = request.Name?.Trim();
        student.GuardianName = request.GuardianName?.Trim();
        student.Address = request.Address?.Trim();
        student.Phone = request.Phone?.Trim();
        student.Email = request.Email?.Trim();
        student.PersonalEmail = request.PersonalEmail?.Trim();
        student.DateOfBirth = request.DateOfBirth;
        student.DateOfJoining = request.DateOfJoining;
        student.MonthlyFees = request.MonthlyFees;
        student.AdmissionFees = request.AdmissionFees;
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
    /// yields the original branch-less registration format.
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

        return null;
    }
}
