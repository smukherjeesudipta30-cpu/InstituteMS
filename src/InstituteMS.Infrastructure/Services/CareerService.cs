using InstituteMS.Application.Careers;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Job vacancies Admin advertises on the public site (Receptionist posts, one
/// branch each), and the applications that come in for them. An application
/// raises a dashboard alert for Admin, Editor and SuperAdmin.
/// </summary>
public class CareerService : ICareerService
{
    private const int MaxCvBytes = 3 * 1024 * 1024;
    private const int MaxPhotoBytes = 1024 * 1024;

    /// <summary>How long one person must wait before applying again, for any vacancy.</summary>
    private const int ReapplyAfterMonths = 3;

    private static readonly string[] CvTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "image/jpeg",
        "image/png"
    ];

    private static readonly string[] PhotoTypes = ["image/jpeg", "image/png", "image/webp"];

    private readonly AppDbContext _db;
    private readonly ILogger<CareerService> _logger;

    public CareerService(AppDbContext db, ILogger<CareerService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async Task<IReadOnlyList<VacancyDto>> GetOpenAsync(CancellationToken ct = default)
    {
        var today = Today;
        var rows = await _db.JobVacancies.AsNoTracking()
            .Where(v => v.IsActive && (v.ClosesOn == null || v.ClosesOn >= today))
            .OrderBy(v => v.Branch).ThenBy(v => v.ShiftStart)
            .ToListAsync(ct);
        return rows.Select(v => Map(v, 0, 0)).ToList();
    }

    public async Task<Result<int>> ApplyAsync(int vacancyId, JobApplyRequest request, CancellationToken ct = default)
    {
        var today = Today;
        var vacancy = await _db.JobVacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vacancyId, ct);
        if (vacancy is null || !vacancy.IsActive || (vacancy.ClosesOn is { } closes && closes < today))
            return Result<int>.Failure("This vacancy is no longer open.", ResultStatus.NotFound);

        /* One application per person in any three months, whichever vacancy: the
           same CV sent to every branch in a week is one application, not five.
           After three months they may apply again, even for the same post. */
        var phone = request.Phone.Trim();
        var since = DateTime.Now.AddMonths(-ReapplyAfterMonths);
        var last = await _db.JobApplications.AsNoTracking()
            .Where(a => a.Phone == phone && a.AppliedOn > since)
            .OrderByDescending(a => a.AppliedOn)
            .Select(a => (DateTime?)a.AppliedOn)
            .FirstOrDefaultAsync(ct);
        if (last is { } previous)
            return Result<int>.Failure(
                $"An application from this phone number was received on {previous:d MMMM yyyy}. " +
                $"You can apply again from {previous.AddMonths(ReapplyAfterMonths):d MMMM yyyy} — " +
                "the office will contact you about your earlier application.", ResultStatus.Conflict);

        var cv = Decode(request.Cv, CvTypes, MaxCvBytes, "CV", "a PDF, Word document or picture");
        if (!cv.Succeeded) return Result<int>.Failure(cv.Error!);
        var photo = Decode(request.Photo, PhotoTypes, MaxPhotoBytes, "photograph", "a JPEG or PNG picture");
        if (!photo.Succeeded) return Result<int>.Failure(photo.Error!);

        var name = request.Name.Trim();
        var application = new JobApplication
        {
            VacancyId = vacancyId,
            Name = name,
            Phone = phone,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Qualification = request.Qualification.Trim(),
            Experience = Clean(request.Experience),
            Address = Clean(request.Address),
            Message = Clean(request.Message),
            CvFileName = Path.GetFileName(request.Cv!.FileName),
            CvContentType = request.Cv.ContentType,
            CvContent = cv.Value!,
            PhotoContentType = request.Photo!.ContentType,
            PhotoContent = photo.Value!,
            AppliedOn = DateTime.Now
        };
        _db.JobApplications.Add(application);

        // The dashboard alert — Admin, Editor and SuperAdmin see it.
        _db.AdminNotifications.Add(new AdminNotification
        {
            Type = AdminNotificationTypes.JobApplication,
            Title = $"{name} applied for {vacancy.Title} at {vacancy.Branch}" +
                    (string.IsNullOrWhiteSpace(vacancy.ShiftName) ? "" : $" ({vacancy.ShiftName} shift)"),
            StudentName = name,
            CourseName = $"{vacancy.Title} - {vacancy.Branch}",
            LoginId = phone,
            DetailValue = application.Qualification,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Job application {Id} for vacancy {Vacancy} from {Name}", application.Id, vacancyId, name);
        return Result<int>.Success(application.Id);
    }

    /* ================================================================ Admin */

    public async Task<IReadOnlyList<VacancyDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.JobVacancies.AsNoTracking()
            .OrderByDescending(v => v.IsActive).ThenBy(v => v.Branch).ThenByDescending(v => v.CreatedOn)
            .ToListAsync(ct);
        var counts = await _db.JobApplications.AsNoTracking()
            .GroupBy(a => a.VacancyId)
            .Select(g => new { g.Key, All = g.Count(), New = g.Count(a => a.Status == JobApplicationStatuses.New) })
            .ToListAsync(ct);
        return rows.Select(v =>
        {
            var c = counts.FirstOrDefault(x => x.Key == v.Id);
            return Map(v, c?.All ?? 0, c?.New ?? 0);
        }).ToList();
    }

    public async Task<Result<VacancyDto>> SaveAsync(int? id, VacancySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var branch = request.Branch.Trim();
        if (!await _db.Branches.AnyAsync(b => b.Name == branch, ct))
            return Result<VacancyDto>.Failure("That branch was not found.");

        JobVacancy vacancy;
        if (id is null)
        {
            vacancy = new JobVacancy { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.JobVacancies.Add(vacancy);
        }
        else
        {
            var found = await _db.JobVacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (found is null) return Result<VacancyDto>.Failure("That vacancy no longer exists.", ResultStatus.NotFound);
            vacancy = found;
            vacancy.ModifiedBy = actor;
            vacancy.ModifiedOn = DateTime.Now;
        }

        vacancy.Title = request.Title.Trim();
        vacancy.Branch = branch;
        vacancy.ShiftName = Clean(request.ShiftName);
        vacancy.ShiftStart = request.ShiftStart;
        vacancy.ShiftEnd = request.ShiftEnd;
        vacancy.WorkingDays = Clean(request.WorkingDays);
        vacancy.Qualification = request.Qualification.Trim();
        vacancy.Experience = Clean(request.Experience);
        vacancy.Salary = Clean(request.Salary);
        vacancy.Description = Clean(request.Description);
        vacancy.Openings = request.Openings;
        vacancy.ClosesOn = request.ClosesOn;
        vacancy.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return Result<VacancyDto>.Success(Map(vacancy, 0, 0));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var vacancy = await _db.JobVacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vacancy is null) return Result<bool>.Failure("That vacancy no longer exists.", ResultStatus.NotFound);

        // Applications are a record of people who applied: a vacancy that has
        // them is closed, not deleted.
        if (await _db.JobApplications.AnyAsync(a => a.VacancyId == id, ct))
            return Result<bool>.Failure("People have applied for this vacancy — close it instead of deleting it.", ResultStatus.Conflict);

        _db.JobVacancies.Remove(vacancy);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<JobApplicationDto>> GetApplicationsAsync(int? vacancyId, CancellationToken ct = default)
    {
        var q = _db.JobApplications.AsNoTracking().Where(a => vacancyId == null || a.VacancyId == vacancyId);
        return await q
            .OrderBy(a => a.Status == JobApplicationStatuses.New ? 0 : 1).ThenByDescending(a => a.AppliedOn)
            .Select(a => new JobApplicationDto(
                a.Id, a.VacancyId, a.Vacancy!.Title, a.Vacancy.Branch,
                a.Name, a.Phone, a.Email, a.Qualification, a.Experience, a.Address, a.Message,
                a.CvFileName, a.Status, a.AdminNote, a.AppliedOn, a.ReviewedBy, a.ReviewedOn))
            .ToListAsync(ct);
    }

    public async Task<Result<JobApplicationDto>> SetStatusAsync(
        int id, JobApplicationStatusRequest request, string? actor, CancellationToken ct = default)
    {
        if (!JobApplicationStatuses.All.Contains(request.Status))
            return Result<JobApplicationDto>.Failure("Unknown status.");

        var application = await _db.JobApplications.Include(a => a.Vacancy).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application is null) return Result<JobApplicationDto>.Failure("That application no longer exists.", ResultStatus.NotFound);

        application.Status = request.Status;
        application.AdminNote = Clean(request.AdminNote);
        application.ReviewedBy = actor;
        application.ReviewedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        var a = application;
        return Result<JobApplicationDto>.Success(new JobApplicationDto(
            a.Id, a.VacancyId, a.Vacancy!.Title, a.Vacancy.Branch, a.Name, a.Phone, a.Email, a.Qualification,
            a.Experience, a.Address, a.Message, a.CvFileName, a.Status, a.AdminNote, a.AppliedOn, a.ReviewedBy, a.ReviewedOn));
    }

    public async Task<Result<CareerFileDto>> GetFileAsync(int applicationId, string kind, CancellationToken ct = default)
    {
        var a = await _db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct);
        if (a is null) return Result<CareerFileDto>.Failure("That application no longer exists.", ResultStatus.NotFound);

        var isCv = string.Equals(kind, "cv", StringComparison.OrdinalIgnoreCase);
        var content = isCv ? a.CvContent : a.PhotoContent;
        var type = (isCv ? a.CvContentType : a.PhotoContentType) ?? "application/octet-stream";
        var safeName = string.Concat(a.Name.Where(char.IsLetterOrDigit));
        var fileName = isCv
            ? a.CvFileName ?? $"CV-{safeName}.pdf"
            : $"Photo-{safeName}{(type == "image/png" ? ".png" : ".jpg")}";

        try
        {
            return Result<CareerFileDto>.Success(new CareerFileDto(Convert.FromBase64String(content), type, fileName));
        }
        catch (FormatException)
        {
            return Result<CareerFileDto>.Failure("That file could not be read.", ResultStatus.NotFound);
        }
    }

    /* ============================================================== helpers */

    private static VacancyDto Map(JobVacancy v, int applications, int fresh) => new(
        v.Id, v.Title, v.Branch, v.ShiftName, v.ShiftStart, v.ShiftEnd, v.WorkingDays, v.Qualification,
        v.Experience, v.Salary, v.Description, v.Openings, v.ClosesOn, v.IsActive, applications, fresh);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Checks an upload's type and size and returns its Base64 body, without any data-URL preamble.</summary>
    private static Result<string> Decode(CareerUploadDto? upload, string[] allowed, int maxBytes, string what, string kinds)
    {
        if (upload is null || string.IsNullOrWhiteSpace(upload.Base64))
            return Result<string>.Failure($"Attach your {what}.");

        if (!allowed.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            return Result<string>.Failure($"Your {what} must be {kinds}.");

        var payload = upload.Base64;
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            payload = payload[(comma + 1)..];

        byte[] bytes;
        try { bytes = Convert.FromBase64String(payload); }
        catch (FormatException) { return Result<string>.Failure($"Your {what} could not be read. Please attach it again."); }

        if (bytes.Length == 0) return Result<string>.Failure($"Your {what} is empty.");
        if (bytes.Length > maxBytes)
            return Result<string>.Failure($"Your {what} is {bytes.Length / 1024} KB. Please use one under {maxBytes / 1024 / 1024} MB.");

        return Result<string>.Success(payload);
    }
}
