using InstituteMS.Application.Common;

namespace InstituteMS.Application.Users;

public interface IUserService
{
    Task<PagedResult<UserListItemDto>> GetListAsync(UserQuery query, CancellationToken ct = default);

    Task<Result<UserListItemDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Legacy <c>ufnGetEmailChk</c> — true when the address is already taken.</summary>
    Task<bool> EmailExistsAsync(string email, int? excludingUserId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(UserSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, UserSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Enable/disable an account; disabling stamps <c>End_date</c> as the legacy update did.</summary>
    Task<Result<bool>> SetStatusAsync(int id, bool isActive, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Sets one user's password to a known value so the administrator can pass it on.
    /// Signs that user's other sessions out, as any password change does.
    /// </summary>
    Task<Result<bool>> SetPasswordAsync(int id, string password, string? actor, CancellationToken ct = default);

    Task<IReadOnlyList<UpcomingBirthdayDto>> GetUpcomingBirthdaysAsync(int days, CancellationToken ct = default);

    /// <summary>Distinct role values present in the data, for the dropdown.</summary>
    Task<IReadOnlyList<string>> GetRoleOptionsAsync(CancellationToken ct = default);

    /// <summary>How many accounts hold each of the fixed roles, for the role-wise grid.</summary>
    Task<IReadOnlyList<RoleCountDto>> GetRoleCountsAsync(CancellationToken ct = default);

    /// <summary>The staff member's photograph, or null when none is saved.</summary>
    Task<UserPhotoFile?> GetPhotoAsync(int id, CancellationToken ct = default);

    Task<Result<bool>> SavePhotoAsync(int id, UserPhotoSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeletePhotoAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// A staff member's photo by login id: the one on their User form, else the one
    /// on their Hierarchy chart box. Null when neither exists.
    /// </summary>
    Task<UserPhotoFile?> GetStaffPhotoAsync(string loginId, CancellationToken ct = default);
}
