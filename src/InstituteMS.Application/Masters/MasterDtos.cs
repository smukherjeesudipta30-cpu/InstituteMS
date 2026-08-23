using InstituteMS.Application.Common;

namespace InstituteMS.Application.Masters;

/* ------------------------------------------------------------------ teachers */

public record TeacherListItemDto(
    int Id,
    string? Name,
    string? LoginId,
    string? Phone,
    /// <summary>Active students currently assigned to this teacher, in any of the three slots.</summary>
    int ActiveStudents,
    int TotalStudents,
    /// <summary>Everything ever paid to this teacher out of student fees.</summary>
    decimal TotalEarnings,
    DateOnly? LastPaidOn,
    /// <summary>
    /// True for the stand-in rows the migration created for teachers that had been
    /// deleted while students still referenced them.
    /// </summary>
    bool IsPlaceholder,
    /// <summary>False when the teacher can be removed without orphaning anything.</summary>
    bool IsReferenced);

public record TeacherSaveRequest
{
    public string? Name { get; init; }
    public string? LoginId { get; init; }
    public string? Phone { get; init; }
}

/// <summary>One line of a teacher's earnings (legacy <c>Teacher/Profits</c>, §4.4 T7).</summary>
public record TeacherEarningDto(
    int Id,
    DateOnly? PaidOn,
    string? BillNo,
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    decimal Amount);

public class TeacherQuery : PagedQuery
{
    /// <summary>Hide the migration's stand-in rows. Defaults to showing them.</summary>
    public bool IncludePlaceholders { get; set; } = true;
}

/* ------------------------------------------------------------------ branches */

public record BranchListItemDto(
    int Id,
    string? Name,
    /// <summary>Short code that goes into registration ids — Rajpur becomes <c>Raj</c>.</summary>
    string? Code,
    bool IsActive,
    /// <summary>Students recorded against this branch.</summary>
    int StudentCount,
    /// <summary>Registration ids already issued with this code; the code is then frozen.</summary>
    int IssuedRegistrations);

public record BranchSaveRequest
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
}

/* ----------------------------------------------------------------- contracts */

public interface ITeacherService
{
    Task<PagedResult<TeacherListItemDto>> GetListAsync(TeacherQuery query, CancellationToken ct = default);

    Task<Result<TeacherListItemDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Legacy <c>ufnGetTecChk</c> — true when the login id is already taken.</summary>
    Task<bool> LoginIdExistsAsync(string loginId, int? excludingTeacherId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(TeacherSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, TeacherSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Refuses to delete a teacher that any student or payout still references.</summary>
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    Task<PagedResult<TeacherEarningDto>> GetEarningsAsync(
        int teacherId, DateOnly? from, DateOnly? to, PagedQuery query, CancellationToken ct = default);
}

public interface IBranchService
{
    Task<IReadOnlyList<BranchListItemDto>> GetListAsync(CancellationToken ct = default);

    Task<Result<int>> CreateAsync(BranchSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, BranchSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> SetStatusAsync(int id, bool isActive, CancellationToken ct = default);
}
