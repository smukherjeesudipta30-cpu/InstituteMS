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
    /* The receptionist is included: chasing unpaid fees is their job, and they take
       the payment at the counter when it comes in. Withholding the list of who owes
       what would leave them following up from memory. The CSV download is this same
       endpoint with a larger page size, so it follows automatically.

       This is the one money report they get. Expenses, income, branch performance
       and teacher pay stay with the back office — those are the institute's
       finances, not the front desk's work. */
    [HttpGet("dues")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<DuesReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DuesReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Dues([FromQuery] DuesReportQuery query, CancellationToken ct)
        => Envelope(await _reports.GetDuesAsync(query, ct));

    /// <summary>
    /// Branch-wise pending payments, split into students who have never paid a
    /// monthly fee and those who have fallen behind.
    /// </summary>
    /// <remarks>
    /// Admin and the front desk only, at the owner's instruction — chasing arrears
    /// is the receptionist's daily work. Deliberately not <c>MasterData</c>: that
    /// would include Editor, and this names every student's outstanding balance.
    /// </remarks>
    [HttpGet("pending-payments")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<PendingPaymentsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PendingPaymentsReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PendingPayments(
        [FromQuery] PendingPaymentsQuery query, CancellationToken ct)
        => Envelope(await _reports.GetPendingPaymentsAsync(query, ct));

    /// <summary>
    /// Every fee payment in a date range — monthly installments and admission fees —
    /// with cash-versus-bank and who recorded it, branch-wise, for a receptionist's
    /// month-end reconciliation.
    /// </summary>
    /// <remarks>
    /// Admin and the front desk only, the same rule as pending-payments: this names
    /// who collected what, which is receptionist-facing information, not a teaching
    /// or editorial one.
    /// </remarks>
    [HttpGet("fee-collections")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<FeeCollectionsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FeeCollectionsReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FeeCollections(
        [FromQuery] FeeCollectionsQuery query, CancellationToken ct)
        => Envelope(await _reports.GetFeeCollectionsAsync(query, ct));

    /// <summary>Institute expenses between two dates, with the period total.</summary>
    // Receptionist included at the owner's instruction: the front desk is asked
    // about spending as well as fees. Reading only — entering and deleting an
    // expense stays on the Expenses screen, which is back office.
    [HttpGet("expenses")]
    [Authorize(Roles = UserRoles.MasterData)]
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
    // Admin, the receptionist at the front desk, and the teacher themselves.
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<TeacherEarningsReportDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TeacherEarnings(
        [FromQuery] int teacherId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        /* Who may look up any teacher, rather than only their own record. Admin and
           Receptionist by name — deliberately not IsBackOffice, so an Editor who is
           also a Teacher cannot read every colleague's account through the Editor
           half of their sign-in.

           Note the legacy data holds combined "Receptionist & Teacher" accounts, and
           such a person now sees every teacher's earnings, not just their own. That
           follows directly from the front desk being given this report. */
        // Manual IsInRole checks, not the declarative [Authorize(Roles=...)]
        // pipeline SuperAdminAuthorizationHandler participates in — SuperAdmin
        // has to be named explicitly wherever this controller checks roles by hand.
        var mayViewAnyTeacher =
            User.IsInRole(UserRoles.SuperAdmin) || User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Receptionist);

        if (!mayViewAnyTeacher)
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
    /// Several teachers' accounts for one range — earned, deduction, paid and
    /// balance for each, with every earning line — plus the totals.
    /// </summary>
    /// <remarks>
    /// Admin and the front desk only: unlike <see cref="TeacherEarnings"/>, a
    /// Teacher has no own-record version of this, since it lists colleagues.
    /// </remarks>
    [HttpGet("teachers-earnings")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<TeachersEarningsReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TeachersEarningsReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TeachersEarnings(
        [FromQuery] int[] teacherIds, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Envelope(await _reports.GetTeachersEarningsAsync(teacherIds, from, to, ct));

    /// <summary>The same report as a downloadable PDF.</summary>
    [HttpGet("teachers-earnings/pdf")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TeachersEarningsPdf(
        [FromQuery] int[] teacherIds, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _reports.GetTeachersEarningsPdfAsync(teacherIds, from, to, ct);

        if (!result.Succeeded || result.Value is null)
            return Envelope(Result<bool>.Failure(result.Error!, result.Status));

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>The same report as an Excel workbook.</summary>
    [HttpGet("teachers-earnings/excel")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TeachersEarningsExcel(
        [FromQuery] int[] teacherIds, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _reports.GetTeachersEarningsExcelAsync(teacherIds, from, to, ct);

        if (!result.Succeeded || result.Value is null)
            return Envelope(Result<bool>.Failure(result.Error!, result.Status));

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>
    /// One teacher's honorarium pay slip for a single calendar month, built from
    /// whatever was actually recorded paid that month.
    /// </summary>
    /// <remarks>
    /// Same visibility as <see cref="TeacherEarnings"/>: office staff may request
    /// any teacher, a signed-in teacher only their own.
    /// </remarks>
    [HttpGet("teachers/{teacherId:int}/payslip")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TeacherPayslip(
        int teacherId, [FromQuery] DateOnly month, CancellationToken ct)
    {
        var mayViewAnyTeacher =
            User.IsInRole(UserRoles.SuperAdmin) || User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Receptionist);

        if (!mayViewAnyTeacher)
        {
            var own = await _reports.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);

            if (!own.Succeeded)
                return Envelope(Result<HonorariumPayslipDto>.Failure(own.Error!, own.Status));

            if (teacherId != own.Value)
            {
                return Envelope(Result<HonorariumPayslipDto>.Failure(
                    "You can only download your own pay slip.", ResultStatus.Forbidden));
            }
        }

        return Envelope(await _reports.GetTeacherPayslipAsync(teacherId, month, ct));
    }

    /// <summary>One receptionist's honorarium pay slip for a single calendar month.</summary>
    /// <remarks>
    /// Back office may request anyone's, the same rule as the receptionist
    /// payments list — this names one person's pay, and a receptionist must not
    /// be able to read a colleague's through this. A signed-in Receptionist may
    /// still download their own: the class-level policy already lets them in, so
    /// this checks the id against their own record rather than the endpoint
    /// being closed to them outright.
    /// </remarks>
    [HttpGet("receptionists/{receptionistId:int}/payslip")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Editor},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumPayslipDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceptionistPayslip(
        int receptionistId, [FromQuery] DateOnly month, CancellationToken ct)
    {
        var mayViewAnyReceptionist =
            User.IsInRole(UserRoles.SuperAdmin) || User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Editor);

        if (!mayViewAnyReceptionist)
        {
            if (!User.IsInRole(UserRoles.Receptionist))
                return Envelope(Result<HonorariumPayslipDto>.Failure(
                    "You do not have access to pay slips.", ResultStatus.Forbidden));

            var own = await _reports.ResolveOwnReceptionistIdAsync(CurrentUserName ?? string.Empty, ct);

            if (!own.Succeeded)
                return Envelope(Result<HonorariumPayslipDto>.Failure(own.Error!, own.Status));

            if (receptionistId != own.Value)
            {
                return Envelope(Result<HonorariumPayslipDto>.Failure(
                    "You can only download your own pay slip.", ResultStatus.Forbidden));
            }
        }

        return Envelope(await _reports.GetReceptionistPayslipAsync(receptionistId, month, ct));
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

    /// <summary>
    /// Every student assigned to one teacher, in any of their three slots. A teacher
    /// may only ever request their own list -- the same self-only rule
    /// <see cref="TeacherEarnings"/> already applies, so "My students" on a teacher's
    /// own dashboard can call this endpoint directly rather than needing one of
    /// its own.
    /// </summary>
    [HttpGet("students-by-teacher")]
    [Authorize(Roles = $"{UserRoles.BackOffice},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<StudentsByTeacherReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentsByTeacherReportDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentsByTeacher(
        [FromQuery] StudentsByTeacherQuery query, CancellationToken ct)
    {
        if (!User.IsInRole(UserRoles.SuperAdmin) && !User.IsInRole(UserRoles.Admin) && !User.IsInRole(UserRoles.Receptionist))
        {
            var own = await _reports.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);

            if (!own.Succeeded)
                return Envelope(Result<StudentsByTeacherReportDto>.Failure(own.Error!, own.Status));

            if (query.TeacherId > 0 && query.TeacherId != own.Value)
            {
                return Envelope(Result<StudentsByTeacherReportDto>.Failure(
                    "You can only view your own students.", ResultStatus.Forbidden));
            }

            query.TeacherId = own.Value;
        }

        return Envelope(await _reports.GetStudentsByTeacherAsync(query, ct));
    }

    /// <summary>
    /// Whether the missed-scan-out deduction is currently switched on. Readable by
    /// anyone who can see a Teacher Account report, so the setting the report is
    /// obeying is never a mystery -- a teacher seeing no deduction line should be
    /// able to tell whether that is because they had no misses or because the rule
    /// is simply off for everyone right now.
    /// </summary>
    [HttpGet("payroll-settings")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist},{UserRoles.Teacher}")]
    [ProducesResponseType(typeof(ApiResponse<PayrollSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PayrollSettings(CancellationToken ct)
        => Envelope(Result<PayrollSettingsDto>.Success(await _reports.GetPayrollSettingsAsync(ct)));

    /// <summary>
    /// Switches the missed-scan-out deduction on or off for every teacher's report.
    /// Admin only -- the owner: "always it should depends on Admin."
    /// </summary>
    [HttpPut("payroll-settings")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PayrollSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePayrollSettings(
        [FromBody] UpdatePayrollSettingsRequest request, CancellationToken ct)
        => Envelope(await _reports.UpdatePayrollSettingsAsync(
            request.MissedScanOutDeductionEnabled, request.PayCycleStartDay, CurrentUserName, ct), "Setting saved.");

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
    /// The signed-in student's own photograph. The general photo endpoint is for
    /// the office only (a student must not fetch other students' photos), so a
    /// student's own screens read it from here — the registration id is resolved
    /// from the sign-in, never taken from the caller.
    /// </summary>
    [HttpGet("my-photo")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyPhoto(
        [FromServices] InstituteMS.Application.Students.IStudentService students, CancellationToken ct)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(CurrentUserName ?? string.Empty, ct);
        if (!own.Succeeded)
            return Envelope(Result<object?>.Failure(own.Error!, own.Status));

        var photo = await students.GetPhotoAsync(own.Value!, ct);
        if (!photo.Succeeded)
            return Envelope(Result<object?>.Failure(photo.Error!, photo.Status));

        var (content, contentType, fileName) = photo.Value;
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content, contentType, fileName);
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
    /// The office's side of the same button — issuing a certificate for a student
    /// who is standing at the counter rather than signed in themselves. Same
    /// eligibility (course marked complete, a result on file), same numbering,
    /// same <c>dbo.CertificateIssue</c> log; only who is recorded as having taken
    /// the copy differs.
    /// </summary>
    /// <remarks>
    /// The registration id is carried in the query string, not the route — it is
    /// "AVI/4861/26", and a slash inside a route segment does not match the way a
    /// literal one does.
    /// </remarks>
    [HttpPost("students/certificate")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StudentCertificateDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueCertificateForStudent(
        [FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _reports.IssueCertificateForStudentAsync(registrationId, CurrentUserName, ct));

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
