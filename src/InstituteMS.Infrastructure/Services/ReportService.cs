using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Reports;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The six operational reports from the legacy project (MIGRATION_PLAN §5.9):
/// new admissions, unpaid students, expenses, a teacher's account, a teacher's
/// student roster, and a student's own statement.
///
/// <para>Two things are true of all of them and were not true before:</para>
/// <list type="number">
///   <item>
///     <b>Totals are SQL aggregates.</b> Every legacy report summed rows in the
///     browser with <c>ng-init="setTotalIncome(Emp)"</c>, which fires once per
///     rendered row - so paging or re-sorting the grid silently changed the total,
///     and <c>Math.abs()</c> on each row turned corrections into additions.
///   </item>
///   <item>
///     <b>Parameters are parameters.</b> Each legacy report concatenated dates and
///     ids straight into SQL (defect D1). Dates also went through
///     <c>Convert.ToDateTime(...).ToString()</c> under the server's locale, so a
///     range could be read as month/day or day/month depending on the machine.
///   </item>
/// </list>
/// </summary>
public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly IFeeService _fees;

    public ReportService(AppDbContext db, IFeeService fees)
    {
        _db = db;
        _fees = fees;
    }

    /* ========================================================== admissions */

    /// <summary>
    /// Students who joined between two dates. Replaces
    /// <c>StuReport/GetAllStuDetails</c> + <c>Views/StudentReport/MonthlyIndex.cshtml</c>.
    /// </summary>
    public async Task<Result<AdmissionsReportDto>> GetAdmissionsAsync(
        AdmissionsReportQuery query, CancellationToken ct = default)
    {
        if (query.To < query.From)
            return Result<AdmissionsReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        // The legacy query used an inner join to Course, so a student on a deleted
        // course vanished from the report. A left join keeps the row and shows a
        // blank course, because the admission fee was still taken.
        var q = _db.Students
            .AsNoTracking()
            .Where(s => s.IsActive
                        && s.DateOfJoining != null
                        && s.DateOfJoining >= query.From
                        && s.DateOfJoining <= query.To);

        if (query.CourseId is > 0)
            q = q.Where(s => s.CourseId == query.CourseId);

        var studentCount = await q.CountAsync(ct);
        var totalFees = await q.SumAsync(s => (decimal?)s.AdmissionFees ?? 0m, ct);

        var items = await q
            .OrderBy(s => s.DateOfJoining)
            .ThenBy(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new AdmissionRowDto(
                s.Id,
                s.RegistrationId,
                s.Name,
                s.Phone,
                s.DateOfJoining,
                s.Course != null ? s.Course.Name : null,
                s.AdmissionFees ?? 0m))
            .ToListAsync(ct);

        var rows = PagedResult<AdmissionRowDto>.Create(items, query.Page, query.PageSize, studentCount);

        return Result<AdmissionsReportDto>.Success(new AdmissionsReportDto(
            query.From, query.To, studentCount, totalFees, rows, DateTime.Now));
    }

    /* ================================================================ dues */

    /// <summary>
    /// Active students on a course with no payment in the range. Replaces
    /// <c>usp_NotPaidData</c> (via <c>Income/NotPaid</c>) +
    /// <c>Views/StudentReport/NotPaidIndex.cshtml</c>.
    /// </summary>
    /// <remarks>
    /// The legacy procedure was <c>(paid outside the range) EXCEPT (paid inside it)</c>,
    /// both sides inner-joined to MonthlyFees. The EXCEPT is the real rule and is kept.
    /// The inner join was not a rule but an accident: a student with no fee row at all
    /// matched neither side and so never appeared - the report on unpaid students
    /// silently omitted everyone who had never paid a rupee. They are included here
    /// unless <see cref="DuesReportQuery.IncludeNeverPaid"/> is cleared.
    /// </remarks>
    public async Task<Result<DuesReportDto>> GetDuesAsync(
        DuesReportQuery query, CancellationToken ct = default)
    {
        if (query.CourseId <= 0)
            return Result<DuesReportDto>.Failure("Select a course to report on.");

        if (query.To < query.From)
            return Result<DuesReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var course = await _db.Courses
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Cid == query.CourseId, ct);

        if (course is null)
            return Result<DuesReportDto>.Failure("That course no longer exists.", ResultStatus.NotFound);

        // Registration ids that were paid inside the window - the EXCEPT side.
        var paidInWindow = _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.PaymentDate != null
                        && f.PaymentDate >= query.From
                        && f.PaymentDate <= query.To
                        && f.StudentRegistrationId != null)
            .Select(f => f.StudentRegistrationId!);

        var q = _db.Students
            .AsNoTracking()
            .Where(s => s.IsActive
                        && s.CourseId == query.CourseId
                        && s.RegistrationId != null
                        && !paidInWindow.Contains(s.RegistrationId));

        if (!query.IncludeNeverPaid)
        {
            // Reproduce the legacy row set exactly: only students with a payment
            // history somewhere outside the window.
            var everPaid = _db.MonthlyFees
                .AsNoTracking()
                .Where(f => f.StudentRegistrationId != null)
                .Select(f => f.StudentRegistrationId!);

            q = q.Where(s => everPaid.Contains(s.RegistrationId!));
        }

        var studentCount = await q.CountAsync(ct);

        var page = await q
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new
            {
                s.Id,
                s.RegistrationId,
                s.Name,
                s.Phone,
                s.DateOfJoining,
                CourseName = s.Course != null ? s.Course.Name : null,
                AdmissionFees = s.AdmissionFees ?? 0m
            })
            .ToListAsync(ct);

        // The last payment per student on this page, for the "last paid" and
        // "outstanding" columns the legacy report did not carry. Correlated
        // sub-queries inside the projection above would not translate, so this is a
        // second pass over just the page's registration ids.
        var pageRegIds = page.Select(p => p.RegistrationId!).ToList();

        var lastPayments = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId != null && pageRegIds.Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => new
            {
                RegistrationId = g.Key,
                LastDate = g.Max(f => f.PaymentDate),
                Due = g.OrderByDescending(f => f.PaymentDate).ThenByDescending(f => f.Id)
                       .Select(f => f.NewDue).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = page.Select(p =>
        {
            // SQL Server matched these case-insensitively; string equality in C# does not,
            // and the ledger holds ids in mixed case ("Avi/..." beside "AVI/...").
            var last = lastPayments.FirstOrDefault(
                l => string.Equals(l.RegistrationId, p.RegistrationId, StringComparison.OrdinalIgnoreCase));

            return new DuesRowDto(
                p.Id, p.RegistrationId, p.Name, p.Phone, p.DateOfJoining, p.CourseName,
                p.AdmissionFees,
                last?.LastDate,
                last?.Due ?? 0m,
                last is not null);
        }).ToList();

        // Counted across the whole result set, not just the page on screen.
        var neverPaidCount = await q
            .Where(s => !_db.MonthlyFees.Any(f => f.StudentRegistrationId == s.RegistrationId))
            .CountAsync(ct);

        var totalOutstanding = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId != null
                        && q.Select(s => s.RegistrationId!).Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => g.OrderByDescending(f => f.PaymentDate).ThenByDescending(f => f.Id)
                          .Select(f => f.NewDue).FirstOrDefault() ?? 0m)
            .SumAsync(ct);

        var rows = PagedResult<DuesRowDto>.Create(items, query.Page, query.PageSize, studentCount);

        return Result<DuesReportDto>.Success(new DuesReportDto(
            query.From, query.To, query.CourseId, course.Name,
            studentCount, neverPaidCount, totalOutstanding, rows, DateTime.Now));
    }

    /* ============================================================ expenses */

    /// <summary>
    /// Institute expenses between two dates. Replaces
    /// <c>ExpenseReport/GetAllExpenseDetails</c> + <c>Views/Expense/ExpenseReport.cshtml</c>.
    /// </summary>
    public async Task<Result<ExpenseReportDto>> GetExpensesAsync(
        ExpenseReportQuery query, CancellationToken ct = default)
    {
        if (query.To < query.From)
            return Result<ExpenseReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var q = _db.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate != null
                        && e.ExpenseDate >= query.From
                        && e.ExpenseDate <= query.To);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(e => e.Details != null && EF.Functions.Like(e.Details, $"%{term}%"));
        }

        var entryCount = await q.CountAsync(ct);
        var total = await q.SumAsync(e => (decimal?)e.Amount ?? 0m, ct);

        var items = await q
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(e => new ExpenseRowDto(e.Id, e.Details, e.ExpenseDate, e.Amount ?? 0m))
            .ToListAsync(ct);

        var rows = PagedResult<ExpenseRowDto>.Create(items, query.Page, query.PageSize, entryCount);

        return Result<ExpenseReportDto>.Success(new ExpenseReportDto(
            query.From, query.To, entryCount, total, rows, DateTime.Now));
    }

    /* ==================================================== teacher earnings */

    /// <summary>
    /// One teacher's earnings against payments for a range. Replaces
    /// <c>Income/TeacPaid</c> + <c>TeacherExpense/TeacPaid</c> +
    /// <c>Views/TeacherReport/Index.cshtml</c>.
    /// </summary>
    /// <remarks>
    /// The legacy earnings query required the student to still list this teacher in
    /// one of their three slots (<c>C.TecName = id OR ...</c>) on top of the
    /// <c>TecFees.tecid</c> match. That meant reassigning a student to a different
    /// teacher erased the original teacher's past earnings from their own report.
    /// The <c>TecFees</c> row records who was paid at the time, so it alone decides
    /// here; only the student's current course name is read from the student row.
    /// </remarks>
    public async Task<Result<TeacherEarningsReportDto>> GetTeacherEarningsAsync(
        int teacherId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (teacherId <= 0)
            return Result<TeacherEarningsReportDto>.Failure("Select a teacher to report on.");

        if (to < from)
            return Result<TeacherEarningsReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var teacher = await _db.Teachers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teacherId, ct);

        if (teacher is null)
            return Result<TeacherEarningsReportDto>.Failure("That teacher no longer exists.", ResultStatus.NotFound);

        var earningsQuery = _db.TeacherFees
            .AsNoTracking()
            .Where(f => f.TeacherId == teacherId
                        && f.PaidOn != null
                        && f.PaidOn >= from
                        && f.PaidOn <= to);

        var totalEarned = await earningsQuery.SumAsync(f => (decimal?)f.Salary ?? 0m, ct);

        var earnings = await earningsQuery
            .OrderByDescending(f => f.PaidOn)
            .ThenByDescending(f => f.Id)
            .Select(f => new
            {
                f.StudentRegistrationId,
                f.PaidOn,
                Salary = f.Salary ?? 0m
            })
            .ToListAsync(ct);

        var earningRegIds = earnings
            .Select(e => e.StudentRegistrationId)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToList();

        var students = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId != null && earningRegIds.Contains(s.RegistrationId))
            .Select(s => new
            {
                s.RegistrationId,
                s.Name,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .ToListAsync(ct);

        var earningRows = earnings.Select(e =>
        {
            // TecFees stores registration ids in mixed case, so this join has to ignore it
            // the way the database collation does - otherwise the student name goes blank.
            var student = students.FirstOrDefault(
                s => string.Equals(s.RegistrationId, e.StudentRegistrationId, StringComparison.OrdinalIgnoreCase));

            return new TeacherEarningRowDto(
                student?.Name, e.StudentRegistrationId, student?.CourseName, e.PaidOn, e.Salary);
        }).ToList();

        var paymentsQuery = _db.TeacherExpenses
            .AsNoTracking()
            .Where(x => x.TeacherId == teacherId
                        && x.ExpenseDate != null
                        && x.ExpenseDate >= from
                        && x.ExpenseDate <= to);

        var totalPaid = await paymentsQuery.SumAsync(x => (decimal?)x.Amount ?? 0m, ct);

        var paymentRows = await paymentsQuery
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.Id)
            .Select(x => new TeacherPaymentRowDto(x.Id, x.ExpenseDate, x.Amount ?? 0m))
            .ToListAsync(ct);

        return Result<TeacherEarningsReportDto>.Success(new TeacherEarningsReportDto(
            teacher.Id, teacher.Name, teacher.Phone, from, to,
            totalEarned, totalPaid, totalEarned - totalPaid,
            earningRows, paymentRows, DateTime.Now));
    }

    /* ================================================= students by teacher */

    /// <summary>
    /// Every student assigned to one teacher in any of their three slots. Replaces
    /// <c>StudentTecacherReport</c> + <c>Models/StudentTecacherReport_class.cs</c>.
    /// </summary>
    /// <remarks>
    /// The legacy version returned the whole result set to the browser and paged it
    /// in JavaScript, and its Excel export wrote raw column names as headers
    /// (<c>Stuname</c>, <c>NoTech</c>, <c>TecName1</c>) alongside the teacher ids
    /// those columns hold. Paging is server-side here and the teacher slots are
    /// resolved to names and phone numbers.
    /// </remarks>
    public async Task<Result<StudentsByTeacherReportDto>> GetStudentsByTeacherAsync(
        StudentsByTeacherQuery query, CancellationToken ct = default)
    {
        if (query.TeacherId <= 0)
            return Result<StudentsByTeacherReportDto>.Failure("Select a teacher to report on.");

        var teacher = await _db.Teachers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TeacherId, ct);

        if (teacher is null)
            return Result<StudentsByTeacherReportDto>.Failure("That teacher no longer exists.", ResultStatus.NotFound);

        var q = _db.Students
            .AsNoTracking()
            .Where(s => s.PrimaryTeacherId == query.TeacherId
                        || s.SecondTeacherId == query.TeacherId
                        || s.ThirdTeacherId == query.TeacherId);

        if (query.ActiveOnly)
            q = q.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s =>
                (s.Name != null && EF.Functions.Like(s.Name, $"%{term}%")) ||
                (s.RegistrationId != null && EF.Functions.Like(s.RegistrationId, $"%{term}%")) ||
                (s.Phone != null && EF.Functions.Like(s.Phone, $"%{term}%")));
        }

        var studentCount = await q.CountAsync(ct);

        var items = await q
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new StudentByTeacherRowDto(
                s.Id,
                s.RegistrationId,
                s.Name,
                s.GuardianName,
                s.Address,
                s.Phone,
                s.Email,
                s.DateOfBirth,
                s.DateOfJoining,
                s.Course != null ? s.Course.Name : null,
                s.Course != null ? s.Course.Duration : null,
                s.MonthlyFees ?? 0m,
                s.TechniqueCount,
                s.PrimaryTeacherId == query.TeacherId ? 1
                    : s.SecondTeacherId == query.TeacherId ? 2 : 3,
                s.PrimaryTeacher != null ? s.PrimaryTeacher.Name : null,
                s.PrimaryTeacher != null ? s.PrimaryTeacher.Phone : null,
                s.SecondTeacher != null ? s.SecondTeacher.Name : null,
                s.SecondTeacher != null ? s.SecondTeacher.Phone : null,
                s.ThirdTeacher != null ? s.ThirdTeacher.Name : null,
                s.ThirdTeacher != null ? s.ThirdTeacher.Phone : null,
                s.IsActive,
                s.IsComplete))
            .ToListAsync(ct);

        var rows = PagedResult<StudentByTeacherRowDto>.Create(
            items, query.Page, query.PageSize, studentCount);

        return Result<StudentsByTeacherReportDto>.Success(new StudentsByTeacherReportDto(
            teacher.Id, teacher.Name, studentCount, rows, DateTime.Now));
    }

    /* ==================================================== student statement */

    /// <summary>
    /// A single student's profile, fee ledger and marks. Replaces
    /// <c>StudentReport/getStuDetails</c> + <c>StudentDetails</c> +
    /// <c>Marks/getStuDetails</c>, which the legacy page fetched as three
    /// chained round trips.
    /// </summary>
    public async Task<Result<StudentStatementDto>> GetStudentStatementAsync(
        string registrationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            return Result<StudentStatementDto>.Failure("A registration id is required.");

        var regId = registrationId.Trim();

        // The legacy query accepted active or completed students only, so a student
        // who had left could not see their own history. That rule is kept: the page
        // is theirs while they are on the rolls or have finished the course.
        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == regId && (s.IsActive || s.IsComplete))
            .Select(s => new
            {
                s.Id,
                s.RegistrationId,
                s.Name,
                s.GuardianName,
                s.Phone,
                s.Email,
                s.DateOfJoining,
                s.DateOfExit,
                CourseName = s.Course != null ? s.Course.Name : null,
                CourseDuration = s.Course != null ? s.Course.Duration : null,
                AdmissionFees = s.AdmissionFees ?? 0m,
                MonthlyFees = s.MonthlyFees ?? 0m,
                s.IsActive,
                s.IsComplete
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<StudentStatementDto>.Failure(
                $"No active or completed student found with registration id '{regId}'.", ResultStatus.NotFound);

        var hasPhoto = await _db.StudentPhotos
            .AsNoTracking()
            .AnyAsync(p => p.StudentRegistrationId == regId || p.Description == regId, ct);

        var fees = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId == regId)
            .OrderByDescending(f => f.PaymentDate)
            .ThenByDescending(f => f.Id)
            .Select(f => new
            {
                f.Id,
                f.BillNo,
                f.PaymentDate,
                Paid = f.Installment ?? 0m,
                Instalment = f.OtherInstallment ?? 0m,
                Due = f.NewDue ?? 0m
            })
            .ToListAsync(ct);

        // Fines are stored on their own table keyed by bill number, and
        // Installment already contains the fine, so the instalment-only column is
        // the difference rather than a second charge.
        var billNos = fees.Select(f => f.BillNo).Where(b => !string.IsNullOrEmpty(b)).Distinct().ToList();

        var fines = await _db.LateFees
            .AsNoTracking()
            .Where(l => l.StudentRegistrationId == regId && l.BillNo != null && billNos.Contains(l.BillNo))
            .GroupBy(l => l.BillNo!)
            .Select(g => new { BillNo = g.Key, Fine = g.Sum(l => l.FineAmount ?? 0m) })
            .ToListAsync(ct);

        var payments = fees.Select(f =>
        {
            var fine = f.BillNo is null
                ? 0m
                : fines.FirstOrDefault(
                    x => string.Equals(x.BillNo, f.BillNo, StringComparison.OrdinalIgnoreCase))?.Fine ?? 0m;

            return new StatementPaymentDto(
                f.Id, f.BillNo, f.PaymentDate, f.Paid, f.Paid - fine, fine, f.Due, student.CourseName);
        }).ToList();

        var marks = await _db.Marks
            .AsNoTracking()
            .Where(m => m.StudentRegistrationId == regId)
            .OrderByDescending(m => m.ExamDate)
            .ThenByDescending(m => m.Id)
            .ToListAsync(ct);

        var markDtos = marks.Select(ToStatementMark).ToList();

        return Result<StudentStatementDto>.Success(new StudentStatementDto(
            student.Id,
            student.RegistrationId,
            student.Name,
            student.GuardianName,
            student.Phone,
            student.Email,
            student.DateOfJoining,
            student.DateOfExit,
            student.CourseName,
            student.CourseDuration,
            student.AdmissionFees,
            student.MonthlyFees,
            student.IsActive,
            student.IsComplete,
            hasPhoto,
            payments.Count,
            payments.Sum(p => p.AmountPaid),
            payments.Sum(p => p.Fine),
            payments.FirstOrDefault()?.DueAfter ?? 0m,
            // Months paid = fee-ledger rows, the same rule the course-progress
            // report uses, so the two pages agree.
            payments.Count,
            payments,
            markDtos,
            DateTime.Now));
    }

    /// <summary>
    /// The completion certificate. Replaces <c>Home/getPrintCertificate</c> and the
    /// <c>Views/Home/PrintCertificate.cshtml</c> query-string round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy query inner-joined Marks, so a student with no exam result got an
    /// empty certificate with blank fields rather than a message. That is a real
    /// requirement - there is no grade without marks - so it returns a stated
    /// failure here instead.
    /// </para>
    /// <para>
    /// It also read a row from an unordered join and let the loop overwrite, so
    /// whichever exam SQL happened to return last decided the grade. The most recent
    /// exam is used here, which is at least deterministic.
    /// </para>
    /// </remarks>
    public async Task<Result<StudentCertificateDto>> GetOwnCertificateAsync(
        string email, CancellationToken ct = default)
    {
        var own = await ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<StudentCertificateDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == regId && (s.IsActive || s.IsComplete))
            .Select(s => new
            {
                s.Name,
                s.GuardianName,
                s.RegistrationId,
                s.DateOfJoining,
                s.IsComplete,
                CourseName = s.Course != null ? s.Course.Name : null,
                CourseDuration = s.Course != null ? s.Course.Duration : null
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<StudentCertificateDto>.Failure(
                "No active or completed student record found for this sign-in.", ResultStatus.NotFound);

        var mark = await _db.Marks
            .AsNoTracking()
            .Where(m => m.StudentRegistrationId == regId)
            .OrderByDescending(m => m.ExamDate)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

        if (mark is null)
            return Result<StudentCertificateDto>.Failure(
                "No exam result has been recorded yet, so a certificate cannot be issued.",
                ResultStatus.NotFound);

        var scored = ToStatementMark(mark);

        return Result<StudentCertificateDto>.Success(new StudentCertificateDto(
            student.Name,
            student.GuardianName,
            student.RegistrationId,
            student.CourseName,
            student.CourseDuration,
            student.DateOfJoining,
            scored.ExamDate,
            scored.TotalFullMarks,
            scored.TotalObtainedMarks,
            scored.PercentScored,
            scored.Grade,
            student.IsComplete,
            DateTime.Now));
    }

    /// <summary>
    /// A money receipt for one of the student's own payments — the per-row
    /// <em>Download</em> button on the legacy student report, which posted to
    /// <c>MonthlyCourseFees/getPrint</c> with whatever row object it was handed.
    /// </summary>
    public async Task<Result<FeeReceiptDto>> GetOwnReceiptAsync(
        string email, int feeId, CancellationToken ct = default)
    {
        var own = await ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<FeeReceiptDto>.Failure(own.Error!, own.Status);

        var ownerRegId = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.Id == feeId)
            .Select(f => f.StudentRegistrationId)
            .FirstOrDefaultAsync(ct);

        if (ownerRegId is null)
            return Result<FeeReceiptDto>.Failure("That payment no longer exists.", ResultStatus.NotFound);

        // The legacy page took the row from the browser, so a crafted request could
        // print anyone's receipt. Ownership is checked here.
        if (!string.Equals(ownerRegId, own.Value, StringComparison.OrdinalIgnoreCase))
            return Result<FeeReceiptDto>.Failure(
                "You can only download your own receipts.", ResultStatus.Forbidden);

        return await _fees.GetReceiptAsync(feeId, ct);
    }

    public async Task<Result<int>> ResolveOwnTeacherIdAsync(
        string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<int>.Failure("No signed-in identity to resolve.", ResultStatus.Unauthorized);

        var trimmed = email.Trim();

        var teacherId = await _db.Teachers
            .AsNoTracking()
            .Where(t => t.LoginId == trimmed)
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        return teacherId is null
            ? Result<int>.Failure(
                "No teacher record is linked to this sign-in. Ask the office to check the user id on your teacher record.",
                ResultStatus.NotFound)
            : Result<int>.Success(teacherId.Value);
    }

    public async Task<Result<string>> ResolveOwnRegistrationIdAsync(
        string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<string>.Failure("No signed-in identity to resolve.", ResultStatus.Unauthorized);

        var trimmed = email.Trim();

        // The legacy lookup matched StuEmail only. A student may have been recorded
        // under either address, so both are checked before giving up.
        var regId = await _db.Students
            .AsNoTracking()
            .Where(s => (s.IsActive || s.IsComplete)
                        && (s.Email == trimmed
                            || s.PersonalEmail == trimmed
                            || s.AvijanLoginEmail == trimmed))
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.Id)
            .Select(s => s.RegistrationId)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(regId)
            ? Result<string>.Failure(
                "No student record is linked to this sign-in. Ask the office to check the email on your registration.",
                ResultStatus.NotFound)
            : Result<string>.Success(regId);
    }

    /* ============================================================= helpers */

    private static StatementMarkDto ToStatementMark(Domain.Entities.Mark m)
    {
        var subjects = new List<StatementSubjectDto>();

        void Add(string? subject, int? full, int? obtained)
        {
            if (!string.IsNullOrWhiteSpace(subject))
                subjects.Add(new StatementSubjectDto(subject, full ?? 0, obtained ?? 0));
        }

        Add(m.Subject1, m.FullMarks1, m.ObtainedMarks1);
        Add(m.Subject2, m.FullMarks2, m.ObtainedMarks2);
        Add(m.Subject3, m.FullMarks3, m.ObtainedMarks3);
        Add(m.Subject4, m.FullMarks4, m.ObtainedMarks4);

        var full = subjects.Sum(s => s.FullMarks);
        var obtained = subjects.Sum(s => s.ObtainedMarks);

        var percent = full <= 0
            ? 0m
            : decimal.Round(obtained * 100m / full, 2, MidpointRounding.AwayFromZero);

        return new StatementMarkDto(m.Id, m.ExamDate, subjects, full, obtained, percent, Grade(percent));
    }

    /// <summary>
    /// The certificate grade bands from <c>convertgrade</c> in Scripts/StudentReport.js,
    /// kept exactly: 95 AA, 90 A+, 80 A, 75 B+, 60 B, 50 C, below that D.
    /// </summary>
    /// <remarks>
    /// The legacy function totalled subjects 1 and 2 only, so a four-subject exam was
    /// graded on half its papers. Every filled subject counts here.
    /// </remarks>
    private static string Grade(decimal percent) => percent switch
    {
        >= 95m => "AA",
        >= 90m => "A+",
        >= 80m => "A",
        >= 75m => "B+",
        >= 60m => "B",
        >= 50m => "C",
        _ => "D"
    };
}
