using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Avijan PreSchool's masters. Open to the PreSchool's own staff only — no
/// Computer Centre role is listed — and to SuperAdmin through
/// SuperAdminAuthorizationHandler. Only the PreSchool admin changes them.
/// </summary>
[Route("api/v1/preschool")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolStaff)]
public class PreSchoolController : ApiControllerBase
{
    private readonly IPreSchoolMasterService _masters;
    private readonly IPreSchoolPublicService _public;
    private readonly IPreSchoolHolidayService _holidays;

    public PreSchoolController(IPreSchoolMasterService masters, IPreSchoolPublicService publicPage, IPreSchoolHolidayService holidays)
    {
        _masters = masters;
        _public = publicPage;
        _holidays = holidays;
    }

    /// <summary>The PreSchool's holidays in one year (this year when none is given), in date order.</summary>
    [HttpGet("holidays")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolHolidayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Holidays([FromQuery] int? year, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolHolidayDto>>.Success(
            await _holidays.ListAsync(year ?? DateTime.Today.Year, ct)));

    [HttpPost("holidays")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolHolidayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateHoliday([FromBody] PreSchoolHolidaySaveRequest request, CancellationToken ct)
        => Envelope(await _holidays.CreateAsync(request, CurrentUserName, ct), "Holiday added.");

    [HttpPut("holidays/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolHolidayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHoliday(int id, [FromBody] PreSchoolHolidaySaveRequest request, CancellationToken ct)
        => Envelope(await _holidays.UpdateAsync(id, request, CurrentUserName, ct), "Holiday saved.");

    [HttpDelete("holidays/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteHoliday(int id, CancellationToken ct)
        => Envelope(await _holidays.DeleteAsync(id, ct), "Holiday removed.");

    /// <summary>Admission enquiries from the public PreSchool page, newest first.</summary>
    [HttpGet("enquiries")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolEnquiryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enquiries([FromQuery] bool includeHandled, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolEnquiryDto>>.Success(await _public.ListEnquiriesAsync(includeHandled, ct)));

    /// <summary>Marks an enquiry as called back (or back to open).</summary>
    [HttpPut("enquiries/{id:int}/handled")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolEnquiryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetHandled(int id, [FromQuery] bool handled, CancellationToken ct)
        => Envelope(await _public.SetEnquiryHandledAsync(id, handled, CurrentUserName, ct),
            handled ? "Marked as called back." : "Marked as open.");

    /// <summary>Settings, classes with their subjects and monthly fees, and the grade table.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolOverviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken ct)
        => Envelope(Result<PreSchoolOverviewDto>.Success(await _masters.GetOverviewAsync(ct)));

    [HttpPost("classes")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolClassDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateClass([FromBody] PreSchoolClassSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.CreateClassAsync(request, CurrentUserName, ct), "Class added.");

    [HttpPut("classes/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolClassDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateClass(int id, [FromBody] PreSchoolClassSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.UpdateClassAsync(id, request, CurrentUserName, ct), "Class saved.");

    [HttpPost("subjects")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSubjectDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSubject([FromBody] PreSchoolSubjectSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.CreateSubjectAsync(request, CurrentUserName, ct), "Subject added.");

    [HttpPut("subjects/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSubjectDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubject(int id, [FromBody] PreSchoolSubjectSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.UpdateSubjectAsync(id, request, CurrentUserName, ct), "Subject saved.");

    [HttpPost("fee-heads")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolFeeHeadDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateFeeHead([FromBody] PreSchoolFeeHeadSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.CreateFeeHeadAsync(request, CurrentUserName, ct), "Fee added.");

    [HttpPut("fee-heads/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolFeeHeadDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFeeHead(int id, [FromBody] PreSchoolFeeHeadSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.UpdateFeeHeadAsync(id, request, CurrentUserName, ct), "Fee saved.");

    [HttpPut("grade-bands")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolGradeBandDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveGradeBands([FromBody] PreSchoolGradeBandsSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.SaveGradeBandsAsync(request, CurrentUserName, ct), "Grades saved.");

    [HttpPut("settings")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSettingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings([FromBody] PreSchoolSettingSaveRequest request, CancellationToken ct)
        => Envelope(await _masters.SaveSettingsAsync(request, CurrentUserName, ct), "Settings saved.");
}
