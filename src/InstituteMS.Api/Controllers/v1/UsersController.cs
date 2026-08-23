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
[Route("api/v1/users")]
[Tags("Users")]
[Authorize(Roles = UserRoles.BackOffice)]
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

    /// <summary>Birthdays in the next N days (legacy U6). Defaults to 30.</summary>
    [HttpGet("birthdays/upcoming")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UpcomingBirthdayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpcomingBirthdays([FromQuery] int days = 30, CancellationToken ct = default)
        => Envelope(await _users.GetUpcomingBirthdaysAsync(days is > 0 and <= 365 ? days : 30, ct));

    /// <summary>Creates a user. The password is hashed; it is never stored or returned in clear.</summary>
    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UserSaveRequest request, CancellationToken ct)
        => Envelope(await _users.CreateAsync(request, CurrentUserName, ct), "User created.");

    /// <summary>Updates a user. Leave <c>password</c> empty to keep the existing one.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UserSaveRequest request, CancellationToken ct)
        => Envelope(await _users.UpdateAsync(id, request, CurrentUserName, ct), "User updated.");

    /// <summary>
    /// Sets one user's password to a value the administrator supplies, so it can be
    /// handed to that member of staff.
    /// </summary>
    /// <remarks>
    /// The legacy Users grid rendered the plain-text <c>Password</c> column so an
    /// administrator could read out someone's sign-in. Passwords are BCrypt hashes
    /// now and cannot be reversed, so there is nothing to display: an administrator
    /// who needs to tell a teacher or receptionist their password <em>sets</em> a
    /// known one here and passes that on. The value is hashed on arrival, never
    /// stored or returned in clear text, and the user's other sessions are ended.
    /// </remarks>
    [HttpPost("{id:int}/set-password")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPassword(
        int id, [FromBody] SetPasswordRequest request, CancellationToken ct)
        => Envelope(
            await _users.SetPasswordAsync(id, request.Password, CurrentUserName, ct),
            "Password set. Share it with the user - it cannot be shown again.");

    /// <summary>Enables or disables an account. Disabling also revokes its refresh token.</summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] bool isActive, CancellationToken ct)
        => Envelope(
            await _users.SetStatusAsync(id, isActive, CurrentUserName, ct),
            isActive ? "User activated." : "User deactivated.");
}
