using InstituteMS.Application.Common;
using InstituteMS.Application.Users;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// User administration. Replaces <c>RegisterController</c> +
/// <c>Views/Register/Index.cshtml</c> + <c>Scripts/Users.js</c>
/// (MIGRATION_PLAN §5.11).
/// </summary>
/// <remarks>
/// Receptionist has full control here — same as Admin, deliberately: create,
/// edit, reset the password of, and activate/deactivate any account, Admin
/// accounts included, with no restriction. That is a real, owner-directed
/// choice, not a default; every mutating action below names Admin and
/// Receptionist explicitly rather than inheriting a class-level policy, since
/// stacking a class-level and a method-level <c>[Authorize]</c> in .NET ANDs
/// the two role sets rather than one loosening the other (the Exam Papers
/// duplicate-check bug earlier this session was exactly this).
/// </remarks>
[Route("api/v1/users")]
[Tags("Users")]
[Authorize(Roles = UserRoles.MasterData)]
public class UsersController : ApiControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Paged list. <c>?email=</c> is the legacy "Search User by Email" box.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken ct)
        => Envelope(await _users.GetListAsync(query, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<UserListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Envelope(await _users.GetByIdAsync(id, ct));

    /// <summary>Legacy <c>EmailCheck</c> / <c>ufnGetEmailChk</c>, for live form feedback.</summary>
    [HttpGet("email-exists")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EmailExists(
        [FromQuery] string email, [FromQuery] int? excludingUserId, CancellationToken ct)
        => Envelope(await _users.EmailExistsAsync(email, excludingUserId, ct));

    /// <summary>Role values for the dropdown, including combined ones present in the data.</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Roles(CancellationToken ct)
        => Envelope(await _users.GetRoleOptionsAsync(ct));

    /// <summary>How many accounts hold each role, for the role-wise grid above the table.</summary>
    [HttpGet("role-counts")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RoleCountDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RoleCounts(CancellationToken ct)
        => Envelope(await _users.GetRoleCountsAsync(ct));

    /// <summary>Birthdays in the next N days (legacy U6). Defaults to 30.</summary>
    [HttpGet("birthdays/upcoming")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UpcomingBirthdayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpcomingBirthdays([FromQuery] int days = 30, CancellationToken ct = default)
        => Envelope(await _users.GetUpcomingBirthdaysAsync(days is > 0 and <= 365 ? days : 30, ct));

    /// <summary>Creates a user. The password is hashed; it is never stored or returned in clear.</summary>
    [HttpPost]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UserSaveRequest request, CancellationToken ct)
        => Envelope(await _users.CreateAsync(request, CurrentUserName, ct), "User created.");

    /// <summary>Updates a user. Leave <c>password</c> empty to keep the existing one.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UserSaveRequest request, CancellationToken ct)
        => Envelope(await _users.UpdateAsync(id, request, CurrentUserName, ct), "User updated.");

    /// <summary>
    /// Sets one user's password to a value the administrator supplies, so it can be
    /// handed to that member of staff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <b>staff</b>, the stored password is a BCrypt hash and cannot be read
    /// back, so an administrator who needs to tell a teacher or receptionist their
    /// sign-in <em>sets</em> a known one here and passes that on.
    /// </para>
    /// <para>
    /// For a <b>student</b>, the portal password issued at registration is also kept
    /// in clear on their student row (as production does, so the office can read it
    /// back), and <see cref="UserListItemDto.StudentPassword"/> returns it. Setting a
    /// new one here updates both that copy and the hash, so the two never drift.
    /// </para>
    /// <para>The user's other sessions are ended either way.</para>
    /// </remarks>
    [HttpPost("{id:int}/set-password")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPassword(
        int id, [FromBody] SetPasswordRequest request, CancellationToken ct)
        => Envelope(
            await _users.SetPasswordAsync(id, request.Password, CurrentUserName, ct),
            "Password set. Share it with the user.");

    /// <summary>Enables or disables an account. Disabling also revokes its refresh token.</summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] bool isActive, CancellationToken ct)
        => Envelope(
            await _users.SetStatusAsync(id, isActive, CurrentUserName, ct),
            isActive ? "User activated." : "User deactivated.");
}
