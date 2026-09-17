using InstituteMS.Application.Common;
using InstituteMS.Application.Lookups;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Dropdown data shared by the data-entry screens. Replaces the legacy
/// <c>Course/getDetails</c> and <c>Teacher/getDetails</c> calls that
/// <c>Scripts/Student.js</c> made on page load. The full Course and Teacher CRUD
/// modules (MIGRATION_PLAN §5.3 / §5.4) land with their own screens.
/// </summary>
[Route("api/v1")]
[Tags("Lookups")]
/* MasterData, so the receptionist gets every lookup here rather than only the
   course one. These feed the dropdowns on Student Registration and the two
   enquiry screens, which are the receptionist's screens; leaving the individual
   lookups on BackOffice let the page open and then failed each dropdown with a
   403, which reads as "you do not have permission" on a page they are entitled to.

   Nothing is given away by this. Each list is the read-only name-and-id form of a
   master the receptionist already has the full screen for. */
[Authorize(Roles = UserRoles.MasterData)]
public class LookupsController : ApiControllerBase
{
    private readonly ILookupService _lookups;

    public LookupsController(ILookupService lookups) => _lookups = lookups;

    /// <summary>All courses, with duration so the form can auto-fill it on selection.</summary>
    [HttpGet("courses/lookup")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CourseLookupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Courses(CancellationToken ct)
        => Envelope(await _lookups.GetCoursesAsync(ct));

    /// <summary>
    /// All teachers. Rows flagged <c>isPlaceholder</c> stand in for teachers deleted
    /// from the legacy system while students still referenced them; they sort last.
    /// </summary>
    [HttpGet("teachers/lookup")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeacherLookupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Teachers(CancellationToken ct)
        => Envelope(await _lookups.GetTeachersAsync(ct));

    /// <summary>
    /// Active branches. <c>code</c> is what goes into a new registration id
    /// (<c>AVI/Raj/3463/26</c>), so the form must send the branch rather than free text.
    /// </summary>
    [HttpGet("branches/lookup")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BranchLookupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Branches(CancellationToken ct)
        => Envelope(await _lookups.GetBranchesAsync(ct));

    /// <summary>Duration in months for one course (legacy <c>Home/getCourseDetails</c>).</summary>
    [HttpGet("courses/{courseId:int}/duration")]
    [ProducesResponseType(typeof(ApiResponse<int?>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CourseDuration(int courseId, CancellationToken ct)
        => Envelope(await _lookups.GetCourseDurationAsync(courseId, ct));
}
