using InstituteMS.Application.Common;
using InstituteMS.Application.Enquiries;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The walk-in enquiry register: what the receptionist takes at the counter, and
/// the follow-up report built on it.
/// </summary>
public class EnquiryService : IEnquiryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnquiryService> _logger;

    public EnquiryService(AppDbContext db, ILogger<EnquiryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /* ------------------------------------------------------------------ read */

    public async Task<PagedResult<CourseEnquiryDto>> GetListAsync(
        CourseEnquiryQuery query, CancellationToken ct = default)
    {
        var enquiries = Filtered(query);

        var totalCount = await enquiries.CountAsync(ct);

        var items = await Project(enquiries
                /* Due first, then soonest due, then newest. A receptionist opening
                   this page wants today's calls at the top, not the most recent
                   thing typed. */
                .OrderBy(e => e.NextFollowUpOn == null ? 1 : 0)
                .ThenBy(e => e.NextFollowUpOn)
                .ThenByDescending(e => e.EnquiryDate)
                .ThenByDescending(e => e.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(ct);

        return PagedResult<CourseEnquiryDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<CourseEnquiryDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var enquiry = await Project(_db.CourseEnquiries.Where(e => e.Id == id)).FirstOrDefaultAsync(ct);

        return enquiry is null
            ? Result<CourseEnquiryDto>.Failure("Enquiry not found.", ResultStatus.NotFound)
            : Result<CourseEnquiryDto>.Success(enquiry);
    }

    /* ----------------------------------------------------------------- write */

    public async Task<Result<int>> CreateAsync(
        CourseEnquirySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var referenceError = await ValidateReferencesAsync(request, ct);
        if (referenceError is not null)
            return Result<int>.Failure(referenceError, ResultStatus.BadRequest);

        var today = DateOnly.FromDateTime(DateTime.Today);

        var enquiry = new CourseEnquiry
        {
            StudentName = request.StudentName.Trim(),
            Phone = request.Phone.Trim(),
            Address = Blank(request.Address),
            CourseId = request.CourseId,
            Remarks = Blank(request.Remarks),
            EnquiryDate = request.EnquiryDate ?? today,
            Status = CourseEnquiryStatus.New,
            NextFollowUpOn = request.NextFollowUpOn,
            BranchId = request.BranchId,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.CourseEnquiries.Add(enquiry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Enquiry {Id} taken for {Name} ({Phone}) by {Actor}",
            enquiry.Id, enquiry.StudentName, enquiry.Phone, actor);

        return Result<int>.Success(enquiry.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, CourseEnquirySaveRequest request, string? actor, CancellationToken ct = default)
    {
        var enquiry = await _db.CourseEnquiries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquiry is null)
            return Result<bool>.Failure("Enquiry not found.", ResultStatus.NotFound);

        var referenceError = await ValidateReferencesAsync(request, ct);
        if (referenceError is not null)
            return Result<bool>.Failure(referenceError, ResultStatus.BadRequest);

        enquiry.StudentName = request.StudentName.Trim();
        enquiry.Phone = request.Phone.Trim();
        enquiry.Address = Blank(request.Address);
        enquiry.CourseId = request.CourseId;
        enquiry.Remarks = Blank(request.Remarks);
        enquiry.BranchId = request.BranchId;

        if (request.EnquiryDate is { } date) enquiry.EnquiryDate = date;

        /* Status is NOT touched here. It moves through RecordFollowUpAsync, which
           is the only path that also stamps the call and the count - letting the
           edit form set it would produce enquiries marked Converted with no
           follow-up ever recorded. */
        enquiry.NextFollowUpOn = request.NextFollowUpOn;

        enquiry.ModifiedBy = actor;
        enquiry.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> RecordFollowUpAsync(
        int id, CourseEnquiryFollowUpRequest request, string? actor, CancellationToken ct = default)
    {
        var enquiry = await _db.CourseEnquiries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquiry is null)
            return Result<bool>.Failure("Enquiry not found.", ResultStatus.NotFound);

        var today = DateOnly.FromDateTime(DateTime.Today);

        enquiry.Status = request.Status.Trim();
        enquiry.LastFollowUpOn = today;
        enquiry.FollowUpCount++;

        /* Closed means closed: no next date, whatever was sent. The validator
           refuses one, and this makes sure a status change that closes an enquiry
           also clears any date already sitting on the row. */
        enquiry.NextFollowUpOn = enquiry.IsOpen ? request.NextFollowUpOn : null;

        if (!string.IsNullOrWhiteSpace(request.ConvertedStudentRegistrationId))
            enquiry.ConvertedStudentRegistrationId = request.ConvertedStudentRegistrationId.Trim();

        /* Appended, never replaced. The remarks are the history of the enquiry -
           overwriting them would throw away what the last three calls established
           to record what this one did. */
        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            var line = $"[{today:dd MMM yyyy}] {request.Remarks.Trim()}";

            enquiry.Remarks = string.IsNullOrWhiteSpace(enquiry.Remarks)
                ? line
                : $"{enquiry.Remarks}\n{line}";

            // The column holds 1000 characters; keep the newest history, not the oldest.
            if (enquiry.Remarks.Length > 1000)
                enquiry.Remarks = enquiry.Remarks[^1000..];
        }

        enquiry.ModifiedBy = actor;
        enquiry.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Enquiry {Id} followed up by {Actor}: now {Status}, next {Next}",
            id, actor, enquiry.Status, enquiry.NextFollowUpOn);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var enquiry = await _db.CourseEnquiries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquiry is null)
            return Result<bool>.Failure("Enquiry not found.", ResultStatus.NotFound);

        _db.CourseEnquiries.Remove(enquiry);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ---------------------------------------------------------------- report */

    public async Task<CourseEnquiryReportDto> GetReportAsync(
        CourseEnquiryQuery query, CancellationToken ct = default)
    {
        var enquiries = Filtered(query);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var rows = await Project(enquiries
                .OrderByDescending(e => e.EnquiryDate)
                .ThenByDescending(e => e.Id))
            .ToListAsync(ct);

        var total = rows.Count;
        var converted = rows.Count(r => r.Status == CourseEnquiryStatus.Converted);

        var totals = new CourseEnquiryTotalsDto(
            total,
            rows.Count(r => r.Status == CourseEnquiryStatus.New),
            rows.Count(r => r.Status == CourseEnquiryStatus.Following),
            converted,
            rows.Count(r => r.Status == CourseEnquiryStatus.NotInterested),
            rows.Count(r => r.IsDue),
            total == 0 ? 0m : Math.Round(converted * 100m / total, 1));

        /* Grouped in memory rather than by a second query: the report is already
           materialised above, and an institute's enquiries for a month are tens of
           rows, not thousands. */
        var byCourse = rows
            .GroupBy(r => new { r.CourseId, Name = r.CourseName ?? "Not specified" })
            .Select(g => new CourseEnquiryCourseTotalDto(
                g.Key.CourseId,
                g.Key.Name,
                g.Count(),
                g.Count(r => r.Status == CourseEnquiryStatus.Converted)))
            .OrderByDescending(c => c.Enquiries)
            .ThenBy(c => c.CourseName)
            .ToList();

        return new CourseEnquiryReportDto(rows, totals, byCourse, query.FromDate, query.ToDate);
    }

    /* --------------------------------------------------------------- helpers */

    private IQueryable<CourseEnquiry> Filtered(CourseEnquiryQuery query)
    {
        var enquiries = _db.CourseEnquiries.AsNoTracking();
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            enquiries = enquiries.Where(e =>
                e.StudentName.Contains(term) || e.Phone.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            enquiries = enquiries.Where(e => e.Status == status);
        }

        if (query.CourseId is > 0) enquiries = enquiries.Where(e => e.CourseId == query.CourseId);
        if (query.BranchId is > 0) enquiries = enquiries.Where(e => e.BranchId == query.BranchId);

        if (query.FromDate is { } from) enquiries = enquiries.Where(e => e.EnquiryDate >= from);
        if (query.ToDate is { } to) enquiries = enquiries.Where(e => e.EnquiryDate <= to);

        if (query.DueOnly)
        {
            enquiries = enquiries.Where(e =>
                (e.Status == CourseEnquiryStatus.New || e.Status == CourseEnquiryStatus.Following) &&
                e.NextFollowUpOn != null && e.NextFollowUpOn <= today);
        }

        return enquiries;
    }

    /// <summary>
    /// One projection for the list, the single read and the report, so the three
    /// cannot disagree about what a row looks like — <c>IsDue</c> in particular.
    /// </summary>
    private IQueryable<CourseEnquiryDto> Project(IQueryable<CourseEnquiry> enquiries)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return enquiries.Select(e => new CourseEnquiryDto(
            e.Id,
            e.StudentName,
            e.Phone,
            e.Address,
            e.CourseId,
            e.Course != null ? e.Course.Name : null,
            e.Remarks,
            e.EnquiryDate,
            e.Status,
            e.NextFollowUpOn,
            e.LastFollowUpOn,
            e.FollowUpCount,
            e.BranchId,
            _db.Branches.Where(b => b.Id == e.BranchId).Select(b => b.Name).FirstOrDefault(),
            e.ConvertedStudentRegistrationId,
            (e.Status == CourseEnquiryStatus.New || e.Status == CourseEnquiryStatus.Following) &&
                e.NextFollowUpOn != null && e.NextFollowUpOn <= today,
            e.CreatedBy,
            e.CreatedOn));
    }

    private async Task<string?> ValidateReferencesAsync(
        CourseEnquirySaveRequest request, CancellationToken ct)
    {
        if (request.CourseId is { } courseId &&
            !await _db.Courses.AnyAsync(c => c.Cid == courseId, ct))
        {
            return "The selected course no longer exists.";
        }

        if (request.BranchId is { } branchId &&
            !await _db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            return "The selected branch no longer exists.";
        }

        return null;
    }

    /// <summary>Empty and whitespace both mean "not given"; store NULL for both.</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
