using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Teacher master. Replaces <c>TeacherController</c> + <c>Views/Teacher/Index.cshtml</c>
/// + <c>Scripts/Teacher.js</c> (MIGRATION_PLAN §5.4).
/// </summary>
[Route("api/v1/teachers")]
[Tags("Teachers")]
[Authorize(Roles = UserRoles.MasterData)]
public class TeachersController : ApiControllerBase
{
    private readonly ITeacherService _teachers;

    public TeachersController(ITeacherService teachers) => _teachers = teachers;

    /// <summary>
    /// Paged list with each teacher's student count and lifetime earnings. Rows
    /// flagged <c>isPlaceholder</c> are stand-ins the migration created for teachers
    /// deleted from the legacy system while students still referenced them.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TeacherListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] TeacherQuery query, CancellationToken ct)
        => Envelope(await _teachers.GetListAsync(query, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<TeacherListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Envelope(await _teachers.GetByIdAsync(id, ct));

    /// <summary>Legacy <c>LogCheck</c> / <c>ufnGetTecChk</c>, for live form feedback.</summary>
    [HttpGet("login-exists")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LoginExists(
        [FromQuery] string loginId, [FromQuery] int? excludingTeacherId, CancellationToken ct)
        => Envelope(await _teachers.LoginIdExistsAsync(loginId, excludingTeacherId, ct));

    /// <summary>Teacher's share of student fees over a period (legacy <c>Profits</c>).</summary>
    [HttpGet("{id:int}/earnings")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TeacherEarningDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Earnings(
        int id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] PagedQuery query,
        CancellationToken ct)
        => Envelope(await _teachers.GetEarningsAsync(id, from, to, query, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] TeacherSaveRequest request, CancellationToken ct)
        => Envelope(await _teachers.CreateAsync(request, CurrentUserName, ct), "Teacher saved.");

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] TeacherSaveRequest request, CancellationToken ct)
        => Envelope(await _teachers.UpdateAsync(id, request, CurrentUserName, ct), "Teacher updated.");

    /// <summary>
    /// Deletes a teacher, but only when nothing references them. The legacy delete
    /// had no such guard, which is how 53 students and 527 payout rows ended up
    /// pointing at teachers that no longer existed (defect T4).
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _teachers.DeleteAsync(id, ct), "Teacher deleted.");

    /* ------------------------------- payments made to the teacher */

    /// <summary>A teacher's account: earned from fees, paid out, and the balance.</summary>
    [HttpGet("{id:int}/account")]
    [ProducesResponseType(typeof(ApiResponse<TeacherAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Account(
        int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Envelope(await _teachers.GetAccountAsync(id, from, to, ct));

    /// <summary>Payments already handed to this teacher.</summary>
    [HttpGet("{id:int}/payments")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeacherPaymentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Payments(
        int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Envelope(await _teachers.GetPaymentsAsync(id, from, to, ct));

    /// <summary>
    /// Records money handed to a teacher — the "paid" side of their account.
    /// </summary>
    /// <remarks>
    /// <b>Administrator only.</b> This moves money out of the institute and
    /// straight onto a teacher's balance, so it is deliberately narrower than the
    /// rest of the teacher master, which Editors may use.
    /// </remarks>
    [HttpPost("{id:int}/payments")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordPayment(
        int id, [FromBody] TeacherPaymentRequest request, CancellationToken ct)
        => Envelope(
            await _teachers.RecordPaymentAsync(request with { TeacherId = id }, CurrentUserName, ct),
            "Payment recorded.");

    /// <summary>Removes a payment recorded in error. Administrator only.</summary>
    [HttpDelete("payments/{paymentId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePayment(int paymentId, CancellationToken ct)
        => Envelope(await _teachers.DeletePaymentAsync(paymentId, ct), "Payment removed.");
}

/// <summary>
/// Branch master. The legacy screen was an empty stub; the code defined here becomes
/// part of every registration id issued for the branch.
/// </summary>
[Route("api/v1/branches")]
[Tags("Branches")]
[Authorize(Roles = UserRoles.MasterData)]
public class BranchesController : ApiControllerBase
{
    private readonly IBranchService _branches;

    public BranchesController(IBranchService branches) => _branches = branches;

    /// <summary>All branches with their student counts and how many ids use each code.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BranchListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Envelope(await _branches.GetListAsync(ct));

    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] BranchSaveRequest request, CancellationToken ct)
        => Envelope(await _branches.CreateAsync(request, CurrentUserName, ct), "Branch saved.");

    /// <summary>
    /// Updates a branch. Renaming carries existing students over; the code is frozen
    /// once any registration id has been issued with it.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] BranchSaveRequest request, CancellationToken ct)
        => Envelope(await _branches.UpdateAsync(id, request, CurrentUserName, ct), "Branch updated.");

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] bool isActive, CancellationToken ct)
        => Envelope(
            await _branches.SetStatusAsync(id, isActive, ct),
            isActive ? "Branch activated." : "Branch deactivated.");
}
