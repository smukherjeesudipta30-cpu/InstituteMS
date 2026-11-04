using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>The PreSchool admin's alerts (see <see cref="IPreSchoolAlertService"/>).</summary>
public class PreSchoolAlertService : IPreSchoolAlertService
{
    private readonly AppDbContext _db;

    public PreSchoolAlertService(AppDbContext db) => _db = db;

    /// <summary>PreSchool alerts this viewer may see: not the PreSchool admin's own changes, unless SuperAdmin.</summary>
    private IQueryable<AdminNotification> Visible(bool viewerIsSuperAdmin)
    {
        var all = _db.AdminNotifications.Where(n => n.Type == AdminNotificationTypes.PreSchoolPasswordChange);
        return viewerIsSuperAdmin ? all : all.Where(n => n.CourseName != UserRoles.PreSchoolHead);
    }

    public async Task<PreSchoolAlertsDto> ListAsync(bool viewerIsSuperAdmin, CancellationToken ct = default)
    {
        var visible = Visible(viewerIsSuperAdmin);
        var unread = await visible.CountAsync(n => n.ReadOn == null, ct);
        var items = await visible.OrderByDescending(n => n.CreatedOn).Take(50)
            .Select(n => new PreSchoolAlertDto(n.Id, n.Type, n.Title, n.LoginId, n.CreatedOn, n.ReadOn != null, n.ReadBy))
            .ToListAsync(ct);
        return new PreSchoolAlertsDto(unread, items);
    }

    public async Task<Result<bool>> MarkReadAsync(int id, string? actor, bool viewerIsSuperAdmin, CancellationToken ct = default)
    {
        var n = await Visible(viewerIsSuperAdmin).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return Result<bool>.Failure("Alert not found.", ResultStatus.NotFound);
        if (n.ReadOn is null)
        {
            n.ReadOn = DateTime.Now;
            n.ReadBy = actor;
            await _db.SaveChangesAsync(ct);
        }
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> MarkAllReadAsync(string? actor, bool viewerIsSuperAdmin, CancellationToken ct = default)
    {
        var unread = await Visible(viewerIsSuperAdmin).Where(n => n.ReadOn == null).ToListAsync(ct);
        var now = DateTime.Now;
        foreach (var n in unread) { n.ReadOn = now; n.ReadBy = actor; }
        await _db.SaveChangesAsync(ct);
        return Result<int>.Success(unread.Count);
    }
}
