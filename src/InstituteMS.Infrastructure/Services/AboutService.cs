using System.Globalization;
using InstituteMS.Application.About;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Public About Us content plus the contact-us submission that replaces the
/// legacy positional <c>Insert into CusMessage values (...)</c> (defect D6).
/// </summary>
public class AboutService : IAboutService
{
    private readonly AppDbContext _db;
    private readonly InstituteProfileOptions _profile;
    private readonly ILogger<AboutService> _logger;

    public AboutService(
        AppDbContext db,
        IOptions<InstituteProfileOptions> profile,
        ILogger<AboutService> logger)
    {
        _db = db;
        _profile = profile.Value;
        _logger = logger;
    }

    public async Task<AboutPageDto> GetAboutPageAsync(CancellationToken ct = default)
    {
        var courses = await _db.Courses
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new AboutCourseDto(
                c.Cid,
                c.Name ?? "(unnamed)",
                c.Duration,
                // Active students only, so a course nobody is currently taking does
                // not read as popular because of people who finished it years ago.
                c.Students.Count(s => s.IsActive == true)))
            .ToListAsync(ct);

        var activeStudents = await _db.Students.CountAsync(s => s.IsActive == true, ct);

        // Active teachers only, so somebody who has left stops being counted as
        // faculty the moment they are marked inactive.
        var teachers = await _db.Teachers.CountAsync(t => t.IsActive, ct);

        /* Counted from the configured founding year rather than the earliest student
           on file. The records only reach back to 2016, so deriving it gave "10+"
           for a centre teaching since 2008 — see InstituteProfileOptions.FoundedYear. */
        var yearsRunning = Math.Max(1, DateTime.Today.Year - _profile.FoundedYear);

        var stats = new List<AboutStatDto>
        {
            new("Active students", activeStudents.ToString(CultureInfo.InvariantCulture), "pi pi-users"),
            new("Courses offered", courses.Count.ToString(CultureInfo.InvariantCulture), "pi pi-book"),
            new("Faculty members", teachers.ToString(CultureInfo.InvariantCulture), "pi pi-id-card"),
            new("Years of teaching", $"{yearsRunning}+", "pi pi-calendar")
        };

        // No photograph means the panel is not ready to show; the front page then
        // renders nothing rather than an empty frame with a missing image in it.
        var desk = string.IsNullOrWhiteSpace(_profile.OwnerDesk.Photo)
            ? null
            : new OwnerDeskDto(
                _profile.OwnerDesk.Name,
                _profile.OwnerDesk.Designation,
                _profile.OwnerDesk.Photo,
                _profile.OwnerDesk.Message,
                _profile.OwnerDesk.Since);

        var contact = new ContactDetailsDto(
            _profile.Contact.AddressLine1,
            _profile.Contact.AddressLine2,
            _profile.Contact.Phone,
            _profile.Contact.Email,
            _profile.Contact.OfficeHours);

        // The front page scrolls the notice board (legacy Notice/getDetails, which
        // the public page called with no authentication). Only the text and date
        // are exposed - nothing identifies a student.
        var notices = await _db.Notices
            .AsNoTracking()
            .Where(n => n.Text != null && n.Text != "")
            .OrderByDescending(n => n.NoticeDate)
            .ThenByDescending(n => n.Id)
            .Take(10)
            .Select(n => new AboutNoticeDto(n.Id, n.Text, n.NoticeDate))
            .ToListAsync(ct);

        // The "Show Timing" panel (legacy Notice/gettime -> dailyRoutine).
        var routine = await _db.DailyRoutines
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoutineRowDto(
                r.Id, r.Name, r.Sunday, r.Monday, r.Tuesday,
                r.Wednesday, r.Thursday, r.Friday, r.Saturday))
            .ToListAsync(ct);

        return new AboutPageDto(
            _profile.Name,
            _profile.Tagline,
            _profile.Introduction,
            _profile.Highlights,
            stats,
            courses,
            contact,
            desk,
            notices,
            routine);
    }

    public async Task<Result<int>> SubmitContactAsync(
        ContactRequest request, string? sourceIp, CancellationToken ct = default)
    {
        var email = request.Email.Trim();

        // Cheap flood guard on a public endpoint: one submission per address per
        // 5 minutes. The rate-limiter in Program.cs covers the per-IP case.
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var recent = await _db.ContactMessages
            .AnyAsync(m => m.Email == email && m.SubmittedOn >= cutoff, ct);

        if (recent)
        {
            return Result<int>.Failure(
                "We already have your recent message — we will get back to you shortly.",
                ResultStatus.TooManyRequests);
        }

        var message = new ContactMessage
        {
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone?.Trim(),
            Body = request.Message.Trim(),
            SubmittedOn = DateTime.UtcNow,
            SourceIp = sourceIp,
            IsHandled = false
        };

        _db.ContactMessages.Add(message);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Contact enquiry {MessageId} received from {Email}", message.Id, email);

        return Result<int>.Success(message.Id);
    }
}
