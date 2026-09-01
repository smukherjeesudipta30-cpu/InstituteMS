using InstituteMS.Application.Lookups;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Backs the dropdowns the student form needs. Replaces the legacy
/// <c>../Course/getDetails</c> and <c>../Teacher/getDetails</c> calls.
/// </summary>
public class LookupService : ILookupService
{
    /// <summary>
    /// Teachers created by the data migration to stand in for rows that were
    /// deleted while students still referenced them (db/migrations/004, T4).
    /// </summary>
    /* Placeholder teachers were created by migration 004 against the migrated
       database. The legacy database has no such rows and dbo.Teacher has no
       CreatedBy column to mark them with, so nothing is treated as a stand-in. */

    private readonly AppDbContext _db;

    public LookupService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CourseLookupDto>> GetCoursesAsync(CancellationToken ct = default) =>
        await _db.Courses
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CourseLookupDto(c.Cid, c.Name ?? "(unnamed)", c.Duration))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TeacherLookupDto>> GetTeachersAsync(CancellationToken ct = default)
    {
        var teachers = await _db.Teachers
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.LoginId,
                IsPlaceholder = false
            })
            .ToListAsync(ct);

        // Placeholders sort last so they stay out of the way when picking a teacher
        // for a new admission, but remain selectable so existing records still bind.
        return teachers
            .OrderBy(t => t.IsPlaceholder)
            .ThenBy(t => t.Name)
            .Select(t => new TeacherLookupDto(t.Id, t.Name ?? "(unnamed)", t.LoginId, t.IsPlaceholder))
            .ToList();
    }

    public async Task<IReadOnlyList<BranchLookupDto>> GetBranchesAsync(CancellationToken ct = default) =>
        await _db.Branches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BranchLookupDto(b.Id, b.Name ?? "(unnamed)", b.Code))
            .ToListAsync(ct);

    public async Task<int?> GetCourseDurationAsync(int courseId, CancellationToken ct = default) =>
        await _db.Courses
            .AsNoTracking()
            .Where(c => c.Cid == courseId)
            .Select(c => c.Duration)
            .FirstOrDefaultAsync(ct);
}
