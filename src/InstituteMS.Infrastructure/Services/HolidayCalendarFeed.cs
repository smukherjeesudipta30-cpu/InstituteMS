using System.Globalization;
using System.Text;
using InstituteMS.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>One holiday read from the calendar feed, ready to become a <see cref="Holiday"/> row.</summary>
public sealed record FeedHoliday(DateOnly Date, string Name, string Category, bool InstituteClosed, string? Note);

/// <summary>
/// Reads a year's holidays from a public iCalendar feed of Indian holidays —
/// by default Google's "Holidays in India" calendar, which needs no key and
/// covers several years ahead. It is an all-India list, so it is narrowed to
/// what matters to a West Bengal institute: every public holiday, plus the
/// Bengal festivals the feed carries only as observances (the Durga Puja days,
/// Saraswati Puja, Doljatra, Poila Baisakh, Bhai Phonta and so on), plus the
/// fixed-date West Bengal days the feed does not carry at all.
/// </summary>
public class HolidayCalendarFeed
{
    public const string DefaultFeedUrl =
        "https://calendar.google.com/calendar/ical/en.indian%23holiday%40group.v.calendar.google.com/public/basic.ics";

    private readonly HttpClient _http;
    private readonly ILogger<HolidayCalendarFeed> _logger;
    private readonly string _url;

    public HolidayCalendarFeed(HttpClient http, IConfiguration configuration, ILogger<HolidayCalendarFeed> logger)
    {
        _http = http;
        _logger = logger;
        _url = string.IsNullOrWhiteSpace(configuration["Holidays:CalendarFeedUrl"])
            ? DefaultFeedUrl
            : configuration["Holidays:CalendarFeedUrl"]!;
    }

    /// <summary>
    /// Feed observances worth keeping, renamed to what people in Bengal call
    /// them, and whether the institute closes by default. Anything else the
    /// feed marks as a mere observance (Pongal, Onam, Ugadi…) is left out.
    /// </summary>
    private static readonly Dictionary<string, (string Name, bool Closed)> Observances = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New Year's Day"] = ("New Year's Day", true),
        ["Vasant Panchami"] = ("Saraswati Puja (Vasant Panchami)", true),
        ["Dolyatra"] = ("Doljatra", true),
        ["Vaisakhadi (Bengal)"] = ("Bengali New Year (Poila Baisakh)", true),
        ["Ambedkar Jayanti"] = ("Birthday of Dr. B. R. Ambedkar", false),
        ["Birthday of Rabindranath"] = ("Rabindranath Tagore Jayanti", true),
        ["Rath Yatra"] = ("Rathayatra", false),
        ["Raksha Bandhan"] = ("Raksha Bandhan", false),
        ["First Day of Durga Puja Festivities"] = ("Durga Puja (Maha Shashthi)", true),
        ["Maha Saptami"] = ("Durga Puja (Maha Saptami)", true),
        ["Maha Ashtami"] = ("Durga Puja (Maha Ashtami)", true),
        ["Maha Navami"] = ("Durga Puja (Maha Nabami)", true),
        ["Bhai Duj"] = ("Bhratridwitiya (Bhai Phonta)", true),
        ["Chhat Puja (Pratihar Sashthi/Surya Sashthi)"] = ("Chhath Puja", false)
    };

    /// <summary>Public holidays kept under a clearer name for Bengal.</summary>
    private static readonly Dictionary<string, string> PublicRenames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dussehra"] = "Durga Puja (Vijaya Dashami)",
        ["Diwali/Deepavali"] = "Kali Puja / Diwali",
        ["Mahatma Gandhi Jayanti"] = "Gandhi Jayanti",
        ["Muharram/Ashura"] = "Muharram",
        ["Ramzan Id"] = "Eid-ul-Fitr",
        ["Bakrid"] = "Id-ud-Zoha (Bakrid)",
        ["Rama Navami"] = "Ram Navami"
    };

    private static readonly HashSet<string> NationalDays = new(StringComparer.OrdinalIgnoreCase)
    {
        "Republic Day", "Independence Day", "Gandhi Jayanti"
    };

    /// <summary>West Bengal holidays on a fixed date every year, which the feed does not carry.</summary>
    private static readonly (int Month, int Day, string Name, bool Closed)[] FixedWestBengalDays =
    [
        (1, 12, "Birthday of Swami Vivekananda", false),
        (1, 23, "Netaji Subhas Chandra Bose Jayanti", true),
        (5, 1, "May Day", false)
    ];

    /// <summary>The year's holidays, or null when the feed could not be read (the caller keeps what it has).</summary>
    public async Task<IReadOnlyList<FeedHoliday>?> FetchYearAsync(int year, CancellationToken ct = default)
    {
        string ics;
        try
        {
            ics = await _http.GetStringAsync(_url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Holiday calendar feed could not be read from {Url}", _url);
            return null;
        }

        var result = new List<FeedHoliday>();
        foreach (var ev in ParseEvents(ics))
        {
            if (ev.Date.Year != year) continue;

            var tentative = ev.Summary.Contains("(tentative)", StringComparison.OrdinalIgnoreCase)
                            || ev.Description.Contains("tentative", StringComparison.OrdinalIgnoreCase);
            var summary = ev.Summary.Replace("(tentative)", "", StringComparison.OrdinalIgnoreCase).Trim();
            var note = tentative ? "Date is tentative (moon sighting) — check the government notification." : null;

            if (ev.Description.StartsWith("Public holiday", StringComparison.OrdinalIgnoreCase))
            {
                var name = PublicRenames.GetValueOrDefault(summary, summary);
                var category = NationalDays.Contains(name) ? HolidayCategories.National : HolidayCategories.Festival;
                result.Add(new FeedHoliday(ev.Date, name, category, true, note));
            }
            else if (Observances.TryGetValue(summary, out var keep))
            {
                result.Add(new FeedHoliday(ev.Date, keep.Name, HolidayCategories.Festival, keep.Closed, note));
            }
        }

        foreach (var (month, day, name, closed) in FixedWestBengalDays)
            result.Add(new FeedHoliday(new DateOnly(year, month, day), name, HolidayCategories.State, closed, null));

        // The feed can list one festival under two names on the same day — keep one row per name.
        return result
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(h => h.Date).First())
            .OrderBy(h => h.Date)
            .ToList();
    }

    private sealed record IcsEvent(DateOnly Date, string Summary, string Description);

    /// <summary>A minimal iCalendar reader — only all-day events' date, summary and description are needed.</summary>
    private static IEnumerable<IcsEvent> ParseEvents(string ics)
    {
        // Unfold continuation lines (RFC 5545 §3.1): a line starting with a space continues the one before.
        var lines = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0)
                lines[^1] += raw[1..];
            else
                lines.Add(raw);
        }

        DateOnly? date = null;
        string summary = "", description = "";
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { date = null; summary = description = ""; continue; }
            if (line == "END:VEVENT")
            {
                if (date is not null && summary.Length > 0) yield return new IcsEvent(date.Value, summary, description);
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon];
            var value = Unescape(line[(colon + 1)..]);

            if (key.StartsWith("DTSTART", StringComparison.Ordinal)
                && DateOnly.TryParseExact(value.Length >= 8 ? value[..8] : value, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                date = d;
            else if (key == "SUMMARY") summary = value.Trim();
            else if (key == "DESCRIPTION") description = value.Trim();
        }
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }
}
