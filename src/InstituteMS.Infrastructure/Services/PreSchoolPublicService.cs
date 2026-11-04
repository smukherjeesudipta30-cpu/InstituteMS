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

        return new PreSchoolPublicDto(
            s?.SchoolName ?? "Avijan PreSchool", s?.Address, s?.Phone, s?.OfficePhone, s?.Email, s?.MapUrl,
            s?.UniformShopName, s?.UniformShopAddress, s?.UniformShopPhone, s?.UniformShopMapUrl, s?.OpensOn,
            classes.Select(c => new PreSchoolPublicClassDto(
                    c.Name, c.MinAgeMonths, c.MaxAgeMonths, c.AdmissionFee,
                    c.FeeHeads.Where(f => f.IsActive).Sum(f => f.MonthlyAmount),
                    c.Subjects.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => x.Name).ToList()))
                .ToList(),
            await _holidays.UpcomingAsync(ct));
    }

    public async Task<Result<bool>> SubmitEnquiryAsync(PreSchoolEnquiryRequest request, string? clientIp, CancellationToken ct = default)
    {
        _db.PreSchoolEnquiries.Add(new PreSchoolEnquiry
        {
            ChildName = request.ChildName.Trim(),
            ChildDateOfBirth = request.ChildDateOfBirth,
            ParentName = request.ParentName.Trim(),
            Phone = request.Phone.Trim(),
            Email = Clean(request.Email),
            ClassWanted = Clean(request.ClassWanted),
            Message = Clean(request.Message),
            ClientIp = clientIp is { Length: > 50 } ? clientIp[..50] : clientIp,
            CreatedOn = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<PreSchoolEnquiryDto>> ListEnquiriesAsync(bool includeHandled, CancellationToken ct = default)
    {
        var query = _db.PreSchoolEnquiries.AsNoTracking();
        if (!includeHandled) query = query.Where(e => !e.IsHandled);

        return await query
            .OrderByDescending(e => e.CreatedOn)
            .Take(500)
            .Select(e => new PreSchoolEnquiryDto(
                e.Id, e.ChildName, e.ChildDateOfBirth, e.ParentName, e.Phone, e.Email,
                e.ClassWanted, e.Message, e.CreatedOn, e.IsHandled, e.HandledBy, e.HandledOn))
            .ToListAsync(ct);
    }

    public async Task<Result<PreSchoolEnquiryDto>> SetEnquiryHandledAsync(int id, bool handled, string? actor, CancellationToken ct = default)
    {
        var e = await _db.PreSchoolEnquiries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Result<PreSchoolEnquiryDto>.Failure("That enquiry no longer exists.", ResultStatus.NotFound);

        e.IsHandled = handled;
        e.HandledBy = handled ? actor : null;
        e.HandledOn = handled ? DateTime.Now : null;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolEnquiryDto>.Success(new PreSchoolEnquiryDto(
            e.Id, e.ChildName, e.ChildDateOfBirth, e.ParentName, e.Phone, e.Email,
            e.ClassWanted, e.Message, e.CreatedOn, e.IsHandled, e.HandledBy, e.HandledOn));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
