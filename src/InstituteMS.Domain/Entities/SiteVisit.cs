namespace InstituteMS.Domain.Entities;

/// <summary>
/// One visit to a public page — maps to <c>dbo.SiteVisit</c>. Kept to see how
/// the site's promotion is working: how many came, to which page, and from where
/// (Google, Facebook, Instagram, WhatsApp, …).
/// </summary>
/// <remarks>
/// No personal data: <see cref="VisitorKey"/> is a one-way hash of the visitor's
/// address and browser with that day's date mixed in, so the same person counts
/// once a day and cannot be followed from one day to the next or identified.
/// </remarks>
public class SiteVisit
{
    public long Id { get; set; }
    public DateTime VisitedOn { get; set; }
    public DateOnly OnDate { get; set; }

    /// <summary>"/", "/careers", "/competition" — the page, without its query string.</summary>
    public string Path { get; set; } = "/";

    /// <summary><see cref="SiteVisitSources"/>.</summary>
    public string Source { get; set; } = SiteVisitSources.Direct;

    /// <summary>"Web" or "App".</summary>
    public string Platform { get; set; } = "Web";

    public string VisitorKey { get; set; } = string.Empty;
}

public static class SiteVisitSources
{
    public const string Google = "Google";
    public const string Facebook = "Facebook";
    public const string Instagram = "Instagram";
    public const string WhatsApp = "WhatsApp";
    public const string OtherSearch = "Other search";
    public const string Direct = "Direct";
    public const string Other = "Other site";
}
