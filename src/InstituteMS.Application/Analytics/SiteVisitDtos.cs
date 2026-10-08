namespace InstituteMS.Application.Analytics;

/// <summary>What the page reports about itself when it loads.</summary>
public record SiteVisitRequest(string Path, string? Referrer, string? UtmSource, string? Platform);

public record SiteVisitDayDto(DateOnly Date, int Visits, int Visitors);

public record SiteVisitCountDto(string Name, int Visits, int Visitors);

/// <summary>The visitor summary Admin sees.</summary>
public record SiteVisitSummaryDto(
    int VisitorsToday,
    int VisitorsLast7Days,
    int VisitorsLast30Days,
    int VisitsLast30Days,
    IReadOnlyList<SiteVisitDayDto> Days,
    IReadOnlyList<SiteVisitCountDto> Sources,
    IReadOnlyList<SiteVisitCountDto> Pages,
    IReadOnlyList<SiteVisitCountDto> Platforms);

public interface ISiteVisitService
{
    /// <summary>Records one visit. Robots and repeats of the same page within half an hour are ignored.</summary>
    Task RecordAsync(SiteVisitRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);

    Task<SiteVisitSummaryDto> GetSummaryAsync(int days, CancellationToken ct = default);
}
