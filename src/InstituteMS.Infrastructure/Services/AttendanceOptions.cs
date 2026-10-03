namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Settings for staff attendance, bound from the <c>Attendance</c> section of
/// appsettings.json.
/// </summary>
public class AttendanceOptions
{
    public const string SectionName = "Attendance";

    /// <summary>
    /// Whether the attendance pages are visible to staff.
    /// </summary>
    /// <remarks>
    /// The endpoints stay reachable when this is false — they are role-guarded
    /// anyway — so the feature can be tested on the live server before anybody sees
    /// it. The flag governs what the front end shows: the menu entries and the
    /// routes. That is what "the site is not disturbed" means in practice.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long one QR code lasts. Short on purpose: a code photographed and sent
    /// to a colleague at home is dead before they can use it.
    /// </summary>
    public int TokenSeconds { get; set; } = 120;

    /// <summary>
    /// A scan closer than this to the previous one is treated as a double tap and
    /// ignored, rather than turning an arrival straight back into a departure.
    /// </summary>
    public int DebounceSeconds { get; set; } = 60;

    /// <summary>
    /// A scan this long after the previous one starts a fresh arrival, whatever
    /// direction the previous scan was.
    /// </summary>
    /// <remarks>
    /// Direction used to be decided by pure alternation: In, then Out, then In, and
    /// so on for the whole day. That broke the moment somebody forgot to scan out —
    /// Nargis scanning in for the morning, going home for lunch without scanning
    /// out, then scanning again on her evening return had that return scan recorded
    /// as "Out" (since the last mark was "In"), and her actual departure that
    /// evening then recorded as "In". Every direction from the missed scan onward
    /// read backwards. A gap this long since the last scan is instead treated as a
    /// new arrival outright, which is what an evening return after a whole afternoon
    /// away actually is.
    /// </remarks>
    public int SessionGapHours { get; set; } = 4;

    /// <summary>
    /// Refuse a fix the phone itself admits is vaguer than this many metres. A
    /// phone indoors on wifi can report a kilometre of uncertainty, and accepting
    /// that would make the fence meaningless.
    /// </summary>
    public int MaxAccuracyMetres { get; set; } = 200;

    /// <summary>
    /// How long an unmatched arrival stays open before the day is written off as
    /// leave.
    /// </summary>
    /// <remarks>
    /// Someone who scans in and never scans out has left no record of when they
    /// went, so there are no hours to count. The owner's rule is that the day then
    /// counts as leave even though the person was present — deliberately strict, so
    /// that scanning out is worth remembering. Until the eight hours are up the day
    /// is still open, which is why somebody halfway through a shift is not marked
    /// absent.
    /// </remarks>
    public int AutoLeaveAfterHours { get; set; } = 8;

    /// <summary>
    /// The least time a teacher must spend at the centre for the day to count as
    /// present — the owner's rule. A finished day under it is counted as absent,
    /// the same as a day with no scan-out. Receptionists are not affected.
    /// </summary>
    public decimal TeacherMinimumHours { get; set; } = 3;

    /// <summary>
    /// The first day the minimum-hours rule applies. Earlier days keep the result
    /// they always had — the rule is not applied to attendance already recorded.
    /// Null applies it to every day.
    /// </summary>
    public DateOnly? TeacherMinimumHoursFrom { get; set; }
}
