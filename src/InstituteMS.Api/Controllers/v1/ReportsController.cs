using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Reports;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The operational reports (MIGRATION_PLAN §5.9). Replaces
/// <c>StuReportController</c>, <c>StudentReportController</c>,
/// <c>ExpenseReportController</c>, <c>TeacherReportController</c>,
/// <c>TeacherDetailsReportController</c> and <c>StudentTecacherReportController</c>
/// - six controllers whose only shared trait was building SQL by concatenation.
/// </summary>
/// <remarks>
/// One legacy report called out to <c>http://www.avijan.in/api/StuReport/...</c>
/// from the browser (Scripts/APIStudent.js), so the admissions report ran against
/// production from whatever machine had the page open, over plain HTTP and with no
/// authentication at all. Every report here is served by this application and
/// requires a token.
/// </remarks>
[Route("api/v1/reports")]
[Tags("Reports")]
[Authorize]
public class ReportsController : ApiControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    /// <summary>Students who joined between two dates, with the admission-fee total.</summary>
    [HttpGet("admissions")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<AdmissionsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AdmissionsReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Admissions([FromQuery] AdmissionsReportQuery query, CancellationToken ct)
        => Envelope(await _reports.GetAdmissionsAsync(query, ct));

    /// <summary>
    /// Active students on a course with no payment in the range, with how much each
    /// still owes.
    /// </summary>
    /// <remarks>
    /// Unlike the legacy <c>usp_NotPaidData</c>, students who have never paid at all
    /// are included. Pass <c>includeNeverPaid=false</c> for the legacy row set.
    /// </remarks>
    [HttpGet("dues")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<DuesReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DuesReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Dues([FromQuery] DuesReportQuery query, CancellationToken ct)
        => Envelope(await _reports.GetDuesAsync(query, ct));

    /// <summary>Institute expenses between two dates, with the period total.</summary>
    [HttpGet("expenses")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Expenses([FromQuery] ExpenseReportQuery query, CancellationToken ct)
        => Envelope(await _reports.GetExpensesAsync(query, ct));

    /// <summary>
    /// One teacher's account for a range: earned from student fees, paid out, and
    /// the balance still pending.
    /// </summary>
    /// <remarks>
    /// The legacy <c>TeacherReport/Index</c> was
    /// <c>[Authorize(Roles = "Editor,Admin,Teacher")]</c>, so a teacher could open
    /// their own account. That is kept: office staff may pass any
    /// <c>teacherId</c>, while a Teacher is pinned to the record whose
    /// <c>LoginId</c> matches their sign-in and gets 403 for anyone else's.
    /// </remarks>
    [HttpGet("teacher-earnings")]
    // Admin and the teacher themselves. Narrower than the legacy
    // "Editor,Admin,Teacher": a teacher's account is what the institute owes a
    // named person, and paying it is an administrator's decision.
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TeacherEarnings(
        [FromQuery] int teacherId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        // Admin, not IsBackOffice. Editors can no longer reach this endpoint at
        // all, and someone holding Editor *and* Teacher would otherwise satisfy
        // IsBackOffice and read every teacher's account through their Teacher role.
        if (!User.IsInRole(UserRoles.Admin))
        {
            var own = await _reports.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);

            if (!own.Succeeded)
                return Envelope(Result<TeacherEarningsReportDto>.Failure(own.Error!, own.Status));

            if (teacherId > 0 && teacherId != own.Value)
            {
                return Envelope(Result<TeacherEarningsReportDto>.Failure(
                    "You can only view your own account.", ResultStatus.Forbidden));
            }

            teacherId = own.Value;
        }

        return Envelope(await _reports.GetTeacherEarningsAsync(teacherId, from, to, ct));
    }

    /// <summary>
    /// The signed-in teacher's own teacher id, resolved from their sign-in the way
    /// the legacy page resolved it from <c>Session["UserID"]</c>.
    /// </summary>
    [HttpGet("my-teacher-id")]
    [Authorize(Roles = $"{UserRoles.BackOffice},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyTeacherId(CancellationToken ct)
        => Envelope(await _reports.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>Every student assigned to one teacher, in any of their three slots.</summary>
    [HttpGet("students-by-teacher")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<StudentsByTeacherReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentsByTeacherReportDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentsByTeacher(
        [FromQuery] StudentsByTeacherQuery query, CancellationToken ct)
        => Envelope(await _reports.GetStudentsByTeacherAsync(query, ct));

    /// <summary>
    /// The signed-in student's own profile, fee ledger and marks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Student role only</b>, exactly as the legacy
    /// <c>StudentReport/Index</c> carried <c>[Authorize(Roles = "Student")]</c>.
    /// This screen shows a student their whole record, so office staff are kept out
    /// of it deliberately - they read the same underlying data through the student
    /// register and the fee ledger instead.
    /// </para>
    /// <para>
    /// The registration id is never taken from the caller. It is resolved from the
    /// signed-in email, so there is no id to tamper with. The legacy page compared
    /// two ids in JavaScript and returned <c>false</c> on a mismatch, which stopped
    /// nothing - the action behind it accepted any id it was posted.
    /// </para>
    /// </remarks>
    [HttpGet("student-statement")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudentStatementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentStatementDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentStatement(CancellationToken ct)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(CurrentUserName ?? string.Empty, ct);

        if (!own.Succeeded)
            return Envelope(Result<StudentStatementDto>.Failure(own.Error!, own.Status));

        return Envelope(await _reports.GetStudentStatementAsync(own.Value!, ct));
    }

    /// <summary>
    /// Admissions, fee payments and expenses by branch, bucketed by month,
    /// quarter or year.
    /// </summary>
    /// <remarks>
    /// An admission and a payment reach a branch through the student; an expense
    /// through the branch recorded on the expense itself. Anything with no branch
    /// is reported as <c>Unassigned</c> rather than dropped, so these totals
    /// reconcile against the unfiltered admission, income and expense reports.
    /// </remarks>
    [HttpGet("branch")]
    // Whole-institute money. The class-level [Authorize] alone would let any
    // signed-in student read it.
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<BranchReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BranchReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Branch([FromQuery] BranchReportQuery query, CancellationToken ct)
        => Envelope(await _reports.GetBranchReportAsync(query, ct));

    /// <summary>
    /// The signed-in student's own registration id, resolved from their email the
    /// way the legacy page resolved it from <c>Session["UserID"]</c>.
    /// </summary>
    /// <summary>
    /// The signed-in student's completion certificate — the <em>Download
    /// Certificate</em> button on the legacy student report.
    /// </summary>
    /// <remarks>
    /// The legacy flow fetched the data, computed the grade in JavaScript, then
    /// passed name, guardian, course, dates and grade through the **query string**
    /// to a printable view. Anyone could edit that URL and print a certificate
    /// naming any person, course and grade they liked. Nothing is passed in here:
    /// every field is read server-side from the signed-in student's own record.
    ///
    /// <para><b>POST, because issuing a certificate is not a read.</b> The first
    /// call returns the original; every call after it returns the next numbered
    /// DUPLICATE COPY, and each is recorded in <c>dbo.CertificateIssue</c>. A GET
    /// would be re-fetched by browsers, caches and refreshes, quietly inflating the
    /// count — and a non-recording variant would be the obvious way to take an
    /// unmarked second copy, so there isn't one.</para>
    /// </remarks>
    [HttpPost("my-certificate")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueMyCertificate(CancellationToken ct)
        => Envelope(await _reports.IssueOwnCertificateAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// Money receipt for one of the signed-in student's own payments — the per-row
    /// <em>Download</em> button on the legacy student report.
    /// </summary>
    [HttpGet("my-receipt")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(typeof(ApiResponse<FeeReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FeeReceiptDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<FeeReceiptDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyReceipt([FromQuery] int feeId, CancellationToken ct)
        => Envelope(await _reports.GetOwnReceiptAsync(CurrentUserName ?? string.Empty, feeId, ct));

    [HttpGet("my-registration-id")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyRegistrationId(CancellationToken ct)
        => Envelope(await _reports.ResolveOwnRegistrationIdAsync(CurrentUserName ?? string.Empty, ct));
}
