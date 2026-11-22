using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The public PreSchool page at /preschool and its admission enquiries. Reads
/// and writes PreSchool* tables only; the Computer Centre's enquiries are untouched.
/// </summary>
public class PreSchoolPublicService : IPreSchoolPublicService
{
    private readonly AppDbContext _db;
    private readonly IPreSchoolHolidayService _holidays;

    public PreSchoolPublicService(AppDbContext db, IPreSchoolHolidayService holidays)
    {
        _db = db;
        _holidays = holidays;
    }

    public async Task<PreSchoolPublicDto> GetPublicAsync(CancellationToken ct = default)
    {
        var s = await _db.PreSchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        var classes = await _db.PreSchoolClasses.AsNoTracking()
            .Where(c => c.IsActive)
            .Include(c => c.Subjects)
            .Include(c => c.FeeHeads)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        // Only the date: the picture itself is served on its own, so this page stays small.
        var smartClassOn = await _db.PreSchoolPhotos.AsNoTracking()
            .Where(p => p.StudentId == null && p.Slot == PreSchoolPhotoSlots.SmartClass)
            .Select(p => (DateTime?)p.UpdatedOn)
            .FirstOrDefaultAsync(ct);

        return new PreSchoolPublicDto(
            s?.SchoolName ?? "Avijan PreSchool", s?.Address, s?.Phone, s?.OfficePhone, s?.Email, s?.MapUrl,
            s?.UniformShopName, s?.UniformShopAddress, s?.UniformShopPhone, s?.UniformShopMapUrl, s?.OpensOn, smartClassOn,
            classes.Select(c => new PreSchoolPublicClassDto(
                    c.Name, c.MinAgeMonths, c.MaxAgeMonths, c.AdmissionFee,
                    c.FeeHeads.Where(f => f.IsActive).Sum(f => f.MonthlyAmount),
                    c.Subjects.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => x.Name).ToList()))
                .ToList(),
            await _holidays.UpcomingAsync(ct));
    }

    public async Task<Result<bool>> SubmitEnquiryAsync(PreSchoolEnquiryRequest request, string? clientIp, CancellationToken ct = default)
    {
        var e = new PreSchoolEnquiry
        {
            ClientIp = clientIp is { Length: > 50 } ? clientIp[..50] : clientIp,
            Source = request.Source is "App" ? "App" : "Website",
            CreatedOn = DateTime.Now
        };
        Apply(e, request);
        _db.PreSchoolEnquiries.Add(e);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<PreSchoolEnquiryDto>> CreateEnquiryAsync(PreSchoolEnquiryRequest request, string? actor, CancellationToken ct = default)
    {
        var e = new PreSchoolEnquiry { Source = "Office", CreatedBy = actor, CreatedOn = DateTime.Now };
        Apply(e, request);
        _db.PreSchoolEnquiries.Add(e);
        await _db.SaveChangesAsync(ct);

        return await GetEnquiryAsync(e.Id, ct);
    }

    public async Task<Result<PreSchoolEnquiryDto>> UpdateEnquiryAsync(int id, PreSchoolEnquiryRequest request, CancellationToken ct = default)
    {
        var e = await _db.PreSchoolEnquiries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Result<PreSchoolEnquiryDto>.Failure("That enquiry no longer exists.", ResultStatus.NotFound);

        Apply(e, request);
        await _db.SaveChangesAsync(ct);
        return await GetEnquiryAsync(id, ct);
    }

    public async Task<IReadOnlyList<PreSchoolEnquiryDto>> ListEnquiriesAsync(bool includeHandled, CancellationToken ct = default)
    {
        var query = _db.PreSchoolEnquiries.AsNoTracking().Include(e => e.Feedback).AsQueryable();
        if (!includeHandled) query = query.Where(e => !e.IsHandled);

        var rows = await query.OrderByDescending(e => e.CreatedOn).Take(500).AsSplitQuery().ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<PreSchoolEnquiryDto>> GetEnquiryAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.PreSchoolEnquiries.AsNoTracking().Include(x => x.Feedback).FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null
            ? Result<PreSchoolEnquiryDto>.Failure("That enquiry no longer exists.", ResultStatus.NotFound)
            : Result<PreSchoolEnquiryDto>.Success(ToDto(e));
    }

    public async Task<Result<PreSchoolEnquiryDto>> SetEnquiryHandledAsync(int id, bool handled, string? actor, CancellationToken ct = default)
    {
        var e = await _db.PreSchoolEnquiries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Result<PreSchoolEnquiryDto>.Failure("That enquiry no longer exists.", ResultStatus.NotFound);

        e.IsHandled = handled;
        e.HandledBy = handled ? actor : null;
        e.HandledOn = handled ? DateTime.Now : null;
        await _db.SaveChangesAsync(ct);

        return await GetEnquiryAsync(id, ct);
    }

    public async Task<Result<PreSchoolEnquiryDto>> AddFeedbackAsync(int enquiryId, PreSchoolEnquiryFeedbackRequest request, string? actor, CancellationToken ct = default)
    {
        if (!await _db.PreSchoolEnquiries.AnyAsync(x => x.Id == enquiryId, ct))
            return Result<PreSchoolEnquiryDto>.Failure("That enquiry no longer exists.", ResultStatus.NotFound);

        _db.PreSchoolEnquiryFeedback.Add(new PreSchoolEnquiryFeedback
        {
            EnquiryId = enquiryId,
            FeedbackOn = request.FeedbackOn ?? DateOnly.FromDateTime(DateTime.Today),
            Note = request.Note.Trim(),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);
        return await GetEnquiryAsync(enquiryId, ct);
    }

    public async Task<Result<PreSchoolEnquiryDto>> DeleteFeedbackAsync(int enquiryId, int feedbackId, CancellationToken ct = default)
    {
        var f = await _db.PreSchoolEnquiryFeedback.FirstOrDefaultAsync(x => x.Id == feedbackId && x.EnquiryId == enquiryId, ct);
        if (f is null) return Result<PreSchoolEnquiryDto>.Failure("That note no longer exists.", ResultStatus.NotFound);

        _db.PreSchoolEnquiryFeedback.Remove(f);
        await _db.SaveChangesAsync(ct);
        return await GetEnquiryAsync(enquiryId, ct);
    }

    /// <summary>
    /// Copies the form onto the row. The person to call back is the one named in
    /// ParentName / Phone (older app versions), else the mother, father or guardian
    /// who gave both a name and a number, in that order.
    /// </summary>
    private static void Apply(PreSchoolEnquiry e, PreSchoolEnquiryRequest r)
    {
        e.ChildName = r.ChildName.Trim();
        e.ChildDateOfBirth = r.ChildDateOfBirth;
        e.ClassWanted = Clean(r.ClassWanted);
        e.Message = Clean(r.Message);
        e.Session = Clean(r.Session);
        e.Gender = Clean(r.Gender);
        e.MotherName = Clean(r.MotherName);
        e.MotherOccupation = Clean(r.MotherOccupation);
        e.MotherPhone = Clean(r.MotherPhone);
        e.MotherEmail = Clean(r.MotherEmail);
        e.MotherAddress = Clean(r.MotherAddress);
        e.FatherName = Clean(r.FatherName);
        e.FatherOccupation = Clean(r.FatherOccupation);
        e.FatherPhone = Clean(r.FatherPhone);
        e.FatherEmail = Clean(r.FatherEmail);
        e.FatherAddress = Clean(r.FatherAddress);
        e.GuardianName = Clean(r.GuardianName);
        e.GuardianOccupation = Clean(r.GuardianOccupation);
        e.GuardianPhone = Clean(r.GuardianPhone);
        e.GuardianAddress = Clean(r.GuardianAddress);
        e.HeardFrom = r.HeardFrom is { Count: > 0 }
            ? string.Join(", ", PreSchoolEnquiryOptions.HeardFrom.Where(h => r.HeardFrom.Contains(h)))
            : null;

        var contact = new[]
            {
                (Name: Clean(r.ParentName), Phone: Clean(r.Phone), Email: Clean(r.Email)),
                (Name: e.MotherName, Phone: e.MotherPhone, Email: e.MotherEmail),
                (Name: e.FatherName, Phone: e.FatherPhone, Email: e.FatherEmail),
                (Name: e.GuardianName, Phone: e.GuardianPhone, Email: (string?)null),
            }
            .FirstOrDefault(c => c.Name is not null && c.Phone is not null);
        e.ParentName = contact.Name ?? e.ParentName;
        e.Phone = contact.Phone ?? e.Phone;
        e.Email = Clean(r.Email) ?? e.MotherEmail ?? e.FatherEmail;
    }

    private static PreSchoolEnquiryDto ToDto(PreSchoolEnquiry e) => new(
        e.Id, e.ChildName, e.ChildDateOfBirth, e.ParentName, e.Phone, e.Email,
        e.ClassWanted, e.Message, e.CreatedOn, e.IsHandled, e.HandledBy, e.HandledOn,
        e.Session, e.Gender,
        e.MotherName, e.MotherOccupation, e.MotherPhone, e.MotherEmail, e.MotherAddress,
        e.FatherName, e.FatherOccupation, e.FatherPhone, e.FatherEmail, e.FatherAddress,
        e.GuardianName, e.GuardianOccupation, e.GuardianPhone, e.GuardianAddress,
        string.IsNullOrEmpty(e.HeardFrom) ? [] : e.HeardFrom.Split(", ", StringSplitOptions.RemoveEmptyEntries),
        e.Source, e.CreatedBy,
        e.Feedback.OrderBy(f => f.FeedbackOn).ThenBy(f => f.Id)
            .Select(f => new PreSchoolEnquiryFeedbackDto(f.Id, f.FeedbackOn, f.Note, f.CreatedBy)).ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
