namespace InstituteMS.Application.Lookups;

/// <summary>Course dropdown entry. Carries Duration so the form can auto-fill it.</summary>
public record CourseLookupDto(int CourseId, string Name, int? Duration);

/// <summary>Teacher dropdown entry.</summary>
public record TeacherLookupDto(int TeacherId, string Name, string? LoginId, bool IsPlaceholder);

/// <summary>
/// Branch dropdown entry. <paramref name="Code"/> is the short form that goes into
/// the registration id (Rajpur → <c>Raj</c>, Baruipur → <c>Bar</c>).
/// </summary>
public record BranchLookupDto(int BranchId, string Name, string? Code);

public interface ILookupService
{
    Task<IReadOnlyList<CourseLookupDto>> GetCoursesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TeacherLookupDto>> GetTeachersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BranchLookupDto>> GetBranchesAsync(CancellationToken ct = default);

    /// <summary>Legacy <c>Home/getCourseDetails</c> — duration for a single course.</summary>
    Task<int?> GetCourseDurationAsync(int courseId, CancellationToken ct = default);
}
