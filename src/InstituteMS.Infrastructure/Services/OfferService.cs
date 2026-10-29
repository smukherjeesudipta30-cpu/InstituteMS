using InstituteMS.Application.Common;
using InstituteMS.Application.Offers;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The admission offer on the public front page: its percentage, and the days it
/// shows. Outside those days (or at 0%) the page shows no ribbon.
/// </summary>
public class OfferService : IOfferService
{
    private const int MaxDays = 366;

    private readonly AppDbContext _db;

    public OfferService(AppDbContext db) => _db = db;

    public async Task<OfferSettingsDto?> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.SiteOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        return s is null ? null : ToDto(s);
    }

    public async Task<Result<OfferSettingsDto>> SaveSettingsAsync(
        OfferSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.Percentage is < 0 or > 100)
            return Result<OfferSettingsDto>.Failure("The offer must be between 0% and 100%.");
        if (request.Days is < 1 or > MaxDays)
            return Result<OfferSettingsDto>.Failure($"Show it for between 1 and {MaxDays} days.");
        if (request.StartsOn == default)
            return Result<OfferSettingsDto>.Failure("Choose the day the offer starts.");

        var row = await _db.SiteOffers.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null)
        {
            row = new SiteOffer { Id = 1 };
            _db.SiteOffers.Add(row);
        }

        row.Percentage = request.Percentage;
        row.StartsOn = request.StartsOn;
        row.Days = request.Days;
        row.ModifiedBy = actor;
        row.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<OfferSettingsDto>.Success(ToDto(row));
    }

    public async Task<OfferPublicDto?> GetPublicAsync(CancellationToken ct = default)
    {
        var s = await _db.SiteOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        return s is not null && IsShowing(s) ? new OfferPublicDto(s.Percentage, s.EndsOn) : null;
    }

    private static bool IsShowing(SiteOffer s)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return s.Percentage > 0 && today >= s.StartsOn && today <= s.EndsOn;
    }

    private static OfferSettingsDto ToDto(SiteOffer s)
        => new(s.Percentage, s.StartsOn, s.Days, s.EndsOn, IsShowing(s), s.ModifiedBy, s.ModifiedOn);
}
