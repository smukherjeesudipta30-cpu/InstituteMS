namespace InstituteMS.Application.About;

/// <summary>Public content for the About Us page. Served anonymously.</summary>
public record AboutPageDto(
    string InstituteName,
    string Tagline,
    string Introduction,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<AboutStatDto> Stats,
    IReadOnlyList<AboutCourseDto> Courses,
    ContactDetailsDto Contact,

    /// <summary>Notice board text, newest first - the front page marquee.</summary>
    IReadOnlyList<AboutNoticeDto> Notices,

    /// <summary>Weekly class routine behind the "Show Timing" panel.</summary>
    IReadOnlyList<RoutineRowDto> Routine);

public record AboutNoticeDto(int Id, string? Text, DateTime? NoticeDate);

public record RoutineRowDto(
    int Id,
    string? Name,
    string? Sunday,
    string? Monday,
    string? Tuesday,
    string? Wednesday,
    string? Thursday,
    string? Friday,
    string? Saturday);

public record AboutStatDto(string Label, string Value, string Icon);

public record AboutCourseDto(int CourseId, string Name, int? DurationMonths);

public record ContactDetailsDto(
    string AddressLine1,
    string AddressLine2,
    string Phone,
    string Email,
    string OfficeHours);

public record ContactRequest(string Name, string Email, string? Phone, string Message);
