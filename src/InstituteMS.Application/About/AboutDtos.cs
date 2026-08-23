namespace InstituteMS.Application.About;

/// <summary>Public content for the About Us page. Served anonymously.</summary>
public record AboutPageDto(
    string InstituteName,
    string Tagline,
    string Introduction,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<AboutStatDto> Stats,
    IReadOnlyList<AboutCourseDto> Courses,
    ContactDetailsDto Contact);

public record AboutStatDto(string Label, string Value, string Icon);

public record AboutCourseDto(int CourseId, string Name, int? DurationMonths);

public record ContactDetailsDto(
    string AddressLine1,
    string AddressLine2,
    string Phone,
    string Email,
    string OfficeHours);

public record ContactRequest(string Name, string Email, string? Phone, string Message);
