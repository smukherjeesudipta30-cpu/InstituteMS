using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The PreSchool's staff and salaries, expenses and income report. The
/// PreSchool admin only (and SuperAdmin through SuperAdminAuthorizationHandler):
/// its receptionist and teachers do not see pay or the school's accounts.
/// </summary>
[Route("api/v1/preschool")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolHead)]
public class PreSchoolFinanceController : ApiControllerBase
{
    private readonly IPreSchoolFinanceService _finance;

    public PreSchoolFinanceController(IPreSchoolFinanceService finance) => _finance = finance;

    [HttpGet("staff")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolStaffDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Staff(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolStaffDto>>.Success(await _finance.ListStaffAsync(ct)));

    [HttpPost("staff")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStaffDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddStaff([FromBody] PreSchoolStaffSaveRequest request, CancellationToken ct)
        => Envelope(await _finance.SaveStaffAsync(null, request, CurrentUserName, ct), "Staff member added.");

    [HttpPut("staff/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStaffDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStaff(int id, [FromBody] PreSchoolStaffSaveRequest request, CancellationToken ct)
        => Envelope(await _finance.SaveStaffAsync(id, request, CurrentUserName, ct), "Staff member saved.");

    /// <summary>The salary sheet for a month ("yyyy-MM-01"; this month when none is given).</summary>
    [HttpGet("salaries")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSalarySheetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SalarySheet([FromQuery] DateOnly? month, CancellationToken ct)
        => Envelope(Result<PreSchoolSalarySheetDto>.Success(
            await _finance.SalarySheetAsync(month ?? DateOnly.FromDateTime(DateTime.Today), ct)));

    [HttpPost("salaries")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSalaryPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PaySalary([FromBody] PreSchoolSalaryPayRequest request, CancellationToken ct)
        => Envelope(await _finance.PaySalaryAsync(request, CurrentUserName, ct), "Salary paid.");

    [HttpGet("salaries/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolSalaryPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SalaryPayment(int id, CancellationToken ct)
        => Envelope(await _finance.GetSalaryPaymentAsync(id, ct));

    [HttpDelete("salaries/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSalaryPayment(int id, CancellationToken ct)
        => Envelope(await _finance.DeleteSalaryPaymentAsync(id, ct), "Salary payment removed.");

    [HttpGet("expenses")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolExpenseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Expenses([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolExpenseDto>>.Success(await _finance.ListExpensesAsync(from, to, ct)));

    [HttpPost("expenses")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolExpenseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddExpense([FromBody] PreSchoolExpenseSaveRequest request, CancellationToken ct)
        => Envelope(await _finance.SaveExpenseAsync(null, request, CurrentUserName, ct), "Expense added.");

    [HttpPut("expenses/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolExpenseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateExpense(int id, [FromBody] PreSchoolExpenseSaveRequest request, CancellationToken ct)
        => Envelope(await _finance.SaveExpenseAsync(id, request, CurrentUserName, ct), "Expense saved.");

    [HttpDelete("expenses/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteExpense(int id, CancellationToken ct)
        => Envelope(await _finance.DeleteExpenseAsync(id, ct), "Expense removed.");

    /// <summary>The PreSchool admin's alerts, e.g. a staff member changed their password; newest first.</summary>
    [HttpGet("alerts")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolAlertsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Alerts([FromServices] IPreSchoolAlertService alerts, CancellationToken ct)
        => Envelope(Result<PreSchoolAlertsDto>.Success(await alerts.ListAsync(User.IsInRole(UserRoles.SuperAdmin), ct)));

    [HttpPatch("alerts/{id:int}/read")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AlertRead(int id, [FromServices] IPreSchoolAlertService alerts, CancellationToken ct)
        => Envelope(await alerts.MarkReadAsync(id, CurrentUserName, User.IsInRole(UserRoles.SuperAdmin), ct), "Marked as read.");

    [HttpPatch("alerts/read-all")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AlertsReadAll([FromServices] IPreSchoolAlertService alerts, CancellationToken ct)
        => Envelope(await alerts.MarkAllReadAsync(CurrentUserName, User.IsInRole(UserRoles.SuperAdmin), ct), "All marked as read.");

    /// <summary>Income, salaries, expenses and net, month by month, for a calendar year.</summary>
    [HttpGet("reports/income")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolIncomeReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Income([FromQuery] int? year, CancellationToken ct)
        => Envelope(Result<PreSchoolIncomeReportDto>.Success(await _finance.IncomeAsync(year ?? DateTime.Today.Year, ct)));

    /// <summary>Income, salaries and expenses between any two dates, with every entry, for downloading.</summary>
    [HttpGet("reports/income-range")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolIncomeRangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> IncomeRange([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Envelope(await _finance.IncomeRangeAsync(from, to, ct));
}
