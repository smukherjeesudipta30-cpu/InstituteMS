using System.Security.Cryptography;
using System.Text;
using InstituteMS.Application.Analytics;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Visits to the public pages, to see how the site's promotion is working. No
/// cookies and no personal data: a visitor is a daily one-way hash, so the same
/// person counts once a day and cannot be identified or followed.
/// </summary>
public class SiteVisitService : ISiteVisitService
{
    /// <summary>The pages counted. Anything else (the signed-in app) is ignored.</summary>
    private static readonly string[] PublicPaths = ["/", "/careers", "/competition"];

    private static readonly string[] RobotMarks =
        ["bot", "crawl", "spider", "slurp", "facebookexternalhit", "whatsapp/", "preview", "headless", "lighthouse"];

    private readonly AppDbContext _db;

    public SiteVisitService(AppDbContext db) => _db = db;

    public async Task RecordAsync(SiteVisitRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var ua = userAgent ?? string.Empty;
        // Link-preview fetchers and search robots are not people.
        if (ua.Length == 0 || RobotMarks.Any(m => ua.Contains(m, StringComparison.OrdinalIgnoreCase))) return;

        var path = NormalisePath(request.Path);
        if (path is null) return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var key = VisitorKey(ipAddress, ua, today);

        // A reload, or coming back to the page within half an hour, is the same visit.
        var recent = now.AddMinutes(-30);
        if (await _db.SiteVisits.AnyAsync(v => v.VisitorKey == key && v.Path == path && v.VisitedOn > recent, ct)) return;

        _db.SiteVisits.Add(new SiteVisit
        {
            VisitedOn = now,
            OnDate = today,
            Path = path,
            Source = SourceOf(request.UtmSource, request.Referrer),
            Platform = string.Equals(request.Platform, "App", StringComparison.OrdinalIgnoreCase) ? "App" : "Web",
            VisitorKey = key
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SiteVisitSummaryDto> GetSummaryAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-(days - 1));

        var rows = await _db.SiteVisits.AsNoTracking()
            .Where(v => v.OnDate >= from)
            .Select(v => new { v.OnDate, v.Path, v.Source, v.Platform, v.VisitorKey })
            .ToListAsync(ct);

        int Unique(IEnumerable<string> keys) => keys.Distinct().Count();
        var since7 = today.AddDays(-6);
        var since30 = today.AddDays(-29);

        var byDay = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d =>
            {
                var day = rows.Where(r => r.OnDate == d).ToList();
                return new SiteVisitDayDto(d, day.Count, Unique(day.Select(r => r.VisitorKey)));
            })
            .Reverse()
            .ToList();

        return new SiteVisitSummaryDto(
            Unique(rows.Where(r => r.OnDate == today).Select(r => r.VisitorKey)),
            Unique(rows.Where(r => r.OnDate >= since7).Select(r => r.VisitorKey)),
            Unique(rows.Where(r => r.OnDate >= since30).Select(r => r.VisitorKey)),
            rows.Count(r => r.OnDate >= since30),
            byDay,
            rows.GroupBy(r => r.Source).Select(g => new SiteVisitCountDto(g.Key, g.Count(), Unique(g.Select(r => r.VisitorKey))))
                .OrderByDescending(c => c.Visits).ToList(),
            rows.GroupBy(r => r.Path).Select(g => new SiteVisitCountDto(PageName(g.Key), g.Count(), Unique(g.Select(r => r.VisitorKey))))
                .OrderByDescending(c => c.Visits).ToList(),
            rows.GroupBy(r => r.Platform).Select(g => new SiteVisitCountDto(g.Key == "App" ? "Mobile app" : "Website", g.Count(), Unique(g.Select(r => r.VisitorKey))))
                .OrderByDescending(c => c.Visits).ToList());
    }

    private static string? NormalisePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var path = raw.Split('?', '#')[0].Trim().ToLowerInvariant();
        if (path.Length > 1) path = path.TrimEnd('/');
        return PublicPaths.Contains(path) ? path : null;
    }

    private static string PageName(string path) => path switch
    {
        "/" => "Home page",
        "/careers" => "Careers",
        "/competition" => "Competition",
        _ => path
    };

    /// <summary>Where the visitor came from: the campaign tag first (utm_source), then the referring site.</summary>
    internal static string SourceOf(string? utmSource, string? referrer)
    {
        var tag = (utmSource ?? string.Empty).ToLowerInvariant();
        var host = string.Empty;
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var uri)) host = uri.Host.ToLowerInvariant();
        var hint = tag.Length > 0 ? tag : host;

        if (hint.Length == 0) return SiteVisitSources.Direct;
        if (hint.Contains("instagram") || hint == "ig") return SiteVisitSources.Instagram;
        if (hint.Contains("facebook") || hint == "fb" || hint.Contains("fb.com") || hint.StartsWith("m.facebook")) return SiteVisitSources.Facebook;
        if (hint.Contains("whatsapp") || hint == "wa") return SiteVisitSources.WhatsApp;
        if (hint.Contains("google")) return SiteVisitSources.Google;
        if (hint.Contains("bing") || hint.Contains("yahoo") || hint.Contains("duckduckgo")) return SiteVisitSources.OtherSearch;
        if (hint.Contains("avijan.in")) return SiteVisitSources.Direct;
        return SiteVisitSources.Other;
    }

    private static string VisitorKey(string? ip, string ua, DateOnly day)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{ip}|{ua}|{day:yyyy-MM-dd}|avijan-visits"));
        return Convert.ToHexString(bytes, 0, 16);
    }
}
