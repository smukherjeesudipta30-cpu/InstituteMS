using System.Globalization;
using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Reports;
using InstituteMS.Domain.Entities;
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
            .Where(s => s.IsActive == true
                        && s.DateOfJoining != null
                        && s.DateOfJoining >= query.From
                        && s.DateOfJoining <= query.To);

        if (query.CourseId is > 0)
            q = q.Where(s => s.CourseId == query.CourseId);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(s => s.Branch == branch);
        }

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

        /* Registration ids that were paid inside the window - the EXCEPT side.

           Keyed on FeeDate, NOT PaymentDate. The two are different things and the
           difference is the whole report: FeeDate (legacy Date1) is when this
           payment was made, while PaymentDate (legacy PDate) holds the date of the
           PREVIOUS payment, which RecordPaymentAsync copies from
           request.PreviousPaymentDate.

           Measured against the live ledger for August 2026: 189 payments fall in
           the month by FeeDate and only 9 by PaymentDate. Testing the wrong column
           left about 180 students who had paid still listed as owing money - and a
           student's FIRST payment has no previous date at all, so anyone paying for
           the first time inside the window could never be excluded. */
        var paidInWindow = _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.FeeDate != null
                        && f.FeeDate >= query.From
                        && f.FeeDate <= query.To
                        && f.StudentRegistrationId != null)
            .Select(f => f.StudentRegistrationId!);

        var q = _db.Students
            .AsNoTracking()
            .Where(s => s.IsActive == true
                        && s.CourseId == query.CourseId
                        && s.RegistrationId != null
                        && !paidInWindow.Contains(s.RegistrationId));

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(s => s.Branch == branch);
        }

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
                AdmissionFees = s.AdmissionFees ?? 0m,
                MonthlyFee = s.MonthlyFees ?? 0m
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

                /* FeeDate for the same reason as the EXCEPT above: PaymentDate
                   carries the PREVIOUS payment's date, so "last paid on" showed the
                   date before the last one, and the due was read off whichever row
                   happened to have the highest previous-date rather than the latest
                   payment. Id breaks the tie, so two payments dated the same day
                   resolve to the one entered last. */
                LastDate = g.Max(f => f.FeeDate),
                Due = g.OrderByDescending(f => f.FeeDate).ThenByDescending(f => f.Id)
                       .Select(f => f.NewDue).FirstOrDefault(),

                // Feeds the fine calculation, which needs to know how many months
                // have already been settled.
                InstallmentsPaid = g.Count()
            })
            .ToListAsync(ct);

        var items = page.Select(p =>
        {
            // SQL Server matched these case-insensitively; string equality in C# does not,
            // and the ledger holds ids in mixed case ("Avi/..." beside "AVI/...").
            var last = lastPayments.FirstOrDefault(
                l => string.Equals(l.RegistrationId, p.RegistrationId, StringComparison.OrdinalIgnoreCase));

            var carried = last?.Due ?? 0m;

            /* What they actually owe as at the report's end date.
               ---------------------------------------------------
               The carried balance alone is nearly always zero: a student who
               settles each bill in full leaves nothing carried, so a report about
               people who have not paid for months showed 0.00 against every one of
               them. What they owe is the months that have gone by unpaid.

               A fee month is 32 days — LateFineCalculator.GraceDays — so the two
               agree about when a month has passed. Counting from the last payment,
               or from joining for a student who has never paid. */
            var reference = last?.LastDate ?? p.DateOfJoining;

            var monthsUnpaid = reference is null
                ? 0
                : Math.Max(0, p.DateOfJoining is null
                    ? 0
                    : (query.To.DayNumber - reference.Value.DayNumber) / LateFineCalculator.GraceDays);

            /* The fine from the shared calculator rather than a second rule here, so
               what the report says a student owes is what the counter would charge
               them on that date. */
            var fine = LateFineCalculator.Calculate(
                query.To, p.DateOfJoining, last?.LastDate, last?.InstallmentsPaid ?? 0).LateFine;

            return new DuesRowDto(
                p.Id, p.RegistrationId, p.Name, p.Phone, p.DateOfJoining, p.CourseName,
                p.AdmissionFees,
                last?.LastDate,
                carried + monthsUnpaid * p.MonthlyFee + fine,
                carried,
                monthsUnpaid,
                p.MonthlyFee,
                fine,
                last is not null);
        }).ToList();

        // Counted across the whole result set, not just the page on screen.
        var neverPaidCount = await q
            .Where(s => !_db.MonthlyFees.Any(f => f.StudentRegistrationId == s.RegistrationId))
            .CountAsync(ct);

        /* The headline total, worked out the same way as each row — carried balance
           plus unpaid months plus the fine. Summing the carried balances alone gave
           the ₹0.00 the rows used to show.

           Materialised over the whole result set rather than aggregated in SQL,
           because the month count and the fine are C# rules that EF cannot
           translate. The set is students on one course who have not paid in a
           range: tens of rows, not thousands. */
        var allStudents = await q
            .Select(s => new
            {
                s.RegistrationId,
                s.DateOfJoining,
                MonthlyFee = s.MonthlyFees ?? 0m
            })
            .ToListAsync(ct);

        var allRegIds = allStudents.Select(s => s.RegistrationId!).ToList();

        var allLastPayments = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId != null && allRegIds.Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => new
            {
                RegistrationId = g.Key,
                LastDate = g.Max(f => f.FeeDate),
                Due = g.OrderByDescending(f => f.FeeDate).ThenByDescending(f => f.Id)
                       .Select(f => f.NewDue).FirstOrDefault(),
                InstallmentsPaid = g.Count()
            })
            .ToListAsync(ct);

        var totalOutstanding = allStudents.Sum(s =>
        {
            var last = allLastPayments.FirstOrDefault(
                l => string.Equals(l.RegistrationId, s.RegistrationId, StringComparison.OrdinalIgnoreCase));

            var carried = last?.Due ?? 0m;
            var reference = last?.LastDate ?? s.DateOfJoining;

            var months = reference is null || s.DateOfJoining is null
                ? 0
                : Math.Max(0, (query.To.DayNumber - reference.Value.DayNumber) / LateFineCalculator.GraceDays);

            var fine = LateFineCalculator.Calculate(
                query.To, s.DateOfJoining, last?.LastDate, last?.InstallmentsPaid ?? 0).LateFine;

            return carried + months * s.MonthlyFee + fine;
        });

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

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(e => e.Ext != null && e.Ext.Branch == branch);
        }

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
            .Select(e => new ExpenseRowDto(
                e.Id, e.Details, e.ExpenseDate, e.Amount ?? 0m,
                e.Ext != null ? e.Ext.Branch : null))
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
            q = q.Where(s => s.IsActive == true);

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
                s.IsActive == true,
                s.IsComplete == true))
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
            .Where(s => s.RegistrationId == regId && (s.IsActive == true || s.IsComplete == true))
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
                IsActive = s.IsActive == true,
                IsComplete = s.IsComplete == true
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

        // The legacy register and the examination portal are two separate stores,
        // and a student may have results in either or both. The statement is the
        // one place that shows their whole record, so it reads both.
        var markDtos = marks.Select(ToStatementMark)
            .Concat(await GetPortalMarksAsync(regId, completeOnly: false, ct))
            .OrderByDescending(m => m.ExamDate)
            .ToList();

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
            student.IsActive == true,
            student.IsComplete == true,
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
    public async Task<Result<StudentCertificateDto>> IssueOwnCertificateAsync(
        string email, CancellationToken ct = default)
    {
        var own = await ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<StudentCertificateDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == regId && (s.IsActive == true || s.IsComplete == true))
            .Select(s => new
            {
                s.Name,
                s.GuardianName,
                s.RegistrationId,
                s.DateOfJoining,
                IsComplete = s.IsComplete == true,
                CourseName = s.Course != null ? s.Course.Name : null,
                CourseDuration = s.Course != null ? s.Course.Duration : null
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<StudentCertificateDto>.Failure(
                "No active or completed student record found for this sign-in.", ResultStatus.NotFound);

        // A certificate says the course was completed, so it is issued only once the
        // course actually is. An earlier version handed out a "provisional" one to a
        // student still enrolled, which the certificate wording does not support.
        if (!student.IsComplete == true)
            return Result<StudentCertificateDto>.Failure(
                "Your certificate becomes available once the course is completed and the " +
                "institute marks your record complete.",
                ResultStatus.Forbidden);

        /* ---- what the certificate certifies ------------------------------
           Results live in two places: the legacy register the Marks screen
           writes, and the examination portal. A student may have either.

           The portal wins when it has a finished examination for them, because
           that is the course they actually sat here; the legacy register is
           the historical record carried over for migrated students. Reading
           only dbo.Marks refused a certificate outright to anyone who sat
           their whole course through the portal. */

        var portalAll = await GetPortalMarksAsync(regId, completeOnly: false, ct);

        // A finished examination carries both its papers, so exactly two subjects.
        var portal = portalAll.Where(m => m.Subjects.Count == 2).ToList();

        StatementMarkDto? scored;

        if (portal.Count > 0)
        {
            // Every finished examination of the course counts, not just the last
            // one — the certificate states the course result, not one term's.
            var subjects = portal.SelectMany(m => m.Subjects).ToList();
            var full = subjects.Sum(s => s.FullMarks);
            var obtained = subjects.Sum(s => s.ObtainedMarks);

            var percent = full <= 0
                ? 0m
                : decimal.Round(obtained * 100m / full, 2, MidpointRounding.AwayFromZero);

            scored = new StatementMarkDto(
                portal[0].MarkId,
                portal.Max(m => m.ExamDate),
                subjects,
                full,
                obtained,
                percent,
                Grade(percent));
        }
        else
        {
            var mark = await _db.Marks
                .AsNoTracking()
                .Where(m => m.StudentRegistrationId == regId)
                .OrderByDescending(m => m.ExamDate)
                .ThenByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);

            if (mark is null)
            {
                // Distinguish "nothing sat" from "sat, but the examination is not
                // finished" — they need different things done about them.
                return Result<StudentCertificateDto>.Failure(
                    portalAll.Count > 0
                        ? "Your examination is not complete yet — the practical mark has " +
                          "still to be entered by the institute, so a certificate cannot be issued."
                        : "No exam result has been recorded yet, so a certificate cannot be issued.",
                    ResultStatus.NotFound);
            }

            scored = ToStatementMark(mark);
        }

        /* ---- number this copy -------------------------------------------
           The first copy a student takes is the original; every one after it
           is a duplicate and prints as one. The number comes from the log
           rather than a counter, so the office can also answer when each copy
           was taken and to which sign-in. */

        var previous = await _db.CertificateIssues
            .AsNoTracking()
            .Where(i => i.StudentRegistrationId == regId)
            .OrderBy(i => i.IssueNo)
            .Select(i => new { i.IssueNo, i.IssuedOn })
            .ToListAsync(ct);

        var issueNo = previous.Count + 1;
        var now = DateTime.Now;

        _db.CertificateIssues.Add(new CertificateIssue
        {
            StudentRegistrationId = regId,
            IssueNo = issueNo,
            IssuedOn = now,
            IssuedTo = email?.Trim()
        });

        await _db.SaveChangesAsync(ct);

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
            student.IsComplete == true,
            now,
            issueNo,
            issueNo > 1,
            previous.FirstOrDefault()?.IssuedOn));
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
            .Where(s => (s.IsActive == true || s.IsComplete == true)
                        && (s.Email == trimmed
                            || s.PersonalEmail == trimmed
                            || s.AvijanLoginEmail == trimmed))
            .OrderByDescending(s => s.IsActive == true)
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

    /// <summary>
    /// The student's examination-portal results, shaped as statement marks — one
    /// entry per examination, carrying Theory and Practical as its subjects.
    /// </summary>
    /// <remarks>
    /// The statement and the certificate originally read <c>dbo.Marks</c> alone,
    /// which is the legacy register the Marks screen writes. A student who sits
    /// everything through the examination portal has no row there at all, so their
    /// statement showed no marks and their certificate was refused outright with
    /// "no exam result has been recorded" — however many papers they had actually
    /// sat. Portal results are read here so both screens see the whole picture.
    ///
    /// <para><paramref name="completeOnly"/> separates two different jobs. The
    /// <b>statement</b> is a record of what has been sat, so it shows every paper
    /// including a term with only its theory in so far — otherwise a mark the
    /// student has genuinely earned is invisible to them. The <b>certificate</b>
    /// states a course result, so it counts finished examinations only: a
    /// half-finished term would otherwise be averaged in as though complete and
    /// drag the grade down.</para>
    ///
    /// <para><c>MarkId</c> is synthetic and negative. It has to be unique for the
    /// grid to track rows, and negative keeps it clearly distinct from a real
    /// <c>dbo.Marks</c> id — nothing looks these up.</para>
    /// </remarks>
    private async Task<List<StatementMarkDto>> GetPortalMarksAsync(
        string regId, bool completeOnly, CancellationToken ct)
    {
        var sat = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.StudentRegistrationId == regId
                        && a.SubmittedOn != null
                        && a.Score != null)
            .Join(_db.ExamPapers.AsNoTracking(),
                a => a.ExamId, p => p.ExamId,
                (a, p) => new
                {
                    a.Score,
                    AttemptTotal = a.TotalMarks,
                    PaperTotal = p.TotalMarks,
                    a.SubmittedOn,
                    PaperType = a.PaperType ?? p.PaperType,
                    TermNumber = a.TermNumber ?? p.TermNumber,
                    CourseId = a.CourseId ?? p.CourseId
                })
            .ToListAsync(ct);

        return sat
            .GroupBy(x => new { x.CourseId, x.TermNumber })
            .Select(g =>
            {
                var theory = g.FirstOrDefault(x =>
                    string.Equals(x.PaperType, "Theory", StringComparison.OrdinalIgnoreCase));

                var practical = g.FirstOrDefault(x =>
                    string.Equals(x.PaperType, "Practical", StringComparison.OrdinalIgnoreCase));

                return new { g.Key, theory, practical };
            })
            // A finished examination has both. The certificate insists; the
            // statement shows whichever papers are in.
            .Where(x => !completeOnly || (x.theory is not null && x.practical is not null))
            .Select(x =>
            {
                // Only the papers actually recorded, so the total never counts
                // marks for a paper that has not been sat or marked yet.
                var subjects = new List<StatementSubjectDto>();

                if (x.theory is not null)
                    subjects.Add(new StatementSubjectDto(
                        "Theory", x.theory.AttemptTotal ?? x.theory.PaperTotal, x.theory.Score ?? 0));

                if (x.practical is not null)
                    subjects.Add(new StatementSubjectDto(
                        "Practical", x.practical.AttemptTotal ?? x.practical.PaperTotal, x.practical.Score ?? 0));

                var full = subjects.Sum(s => s.FullMarks);
                var obtained = subjects.Sum(s => s.ObtainedMarks);

                var percent = full <= 0
                    ? 0m
                    : decimal.Round(obtained * 100m / full, 2, MidpointRounding.AwayFromZero);

                // Either paper may be absent on the statement, so only dated
                // entries are considered.
                var completedOn = new[] { x.theory?.SubmittedOn, x.practical?.SubmittedOn }
                    .Where(d => d is not null)
                    .Max();

                return new StatementMarkDto(
                    -((x.Key.CourseId ?? 0) * 1000 + x.Key.TermNumber),
                    completedOn is null ? null : DateOnly.FromDateTime(completedOn.Value),
                    subjects,
                    full,
                    obtained,
                    percent,
                    Grade(percent));
            })
            .OrderByDescending(m => m.ExamDate)
            .ToList();
    }

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

    /* ========================================================= branch report */

    /// <summary>Everything with no branch recorded reports under this name.</summary>
    private const string Unassigned = "Unassigned";

    private static string BranchOf(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unassigned : value.Trim();

    /// <summary>First day of the bucket a date falls in.</summary>
    private static DateOnly PeriodStartOf(DateOnly date, BranchReportGrouping grouping) => grouping switch
    {
        BranchReportGrouping.Year => new DateOnly(date.Year, 1, 1),
        BranchReportGrouping.Quarter => new DateOnly(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1),
        _ => new DateOnly(date.Year, date.Month, 1)
    };

    private static string PeriodLabelOf(DateOnly start, BranchReportGrouping grouping) => grouping switch
    {
        BranchReportGrouping.Year => start.Year.ToString(CultureInfo.InvariantCulture),
        BranchReportGrouping.Quarter => $"Q{(start.Month - 1) / 3 + 1} {start.Year}",
        _ => start.ToString("MMM yyyy", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Admissions, fee payments and expenses by branch, bucketed by month,
    /// quarter or year.
    /// </summary>
    /// <remarks>
    /// The three measures reach a branch by different routes — an admission and a
    /// payment through the student's own branch, an expense through the branch on
    /// the expense row — so they are gathered separately and stitched together on
    /// (branch, period) rather than joined in SQL.
    ///
    /// <para>Bucketing happens in memory because the boundaries differ per
    /// grouping and translating three shapes of date arithmetic to SQL for three
    /// different sources would cost more in clarity than it saves: the row counts
    /// here are thousands, not millions.</para>
    /// </remarks>
    public async Task<Result<BranchReportDto>> GetBranchReportAsync(
        BranchReportQuery query, CancellationToken ct = default)
    {
        if (query.To < query.From)
            return Result<BranchReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var grouping = query.GroupBy;
        var wanted = string.IsNullOrWhiteSpace(query.Branch) ? null : query.Branch.Trim();

        /* ---- admissions: students who joined in the range ---------------- */
        var admissions = await _db.Students
            .AsNoTracking()
            .Where(s => s.DateOfJoining != null
                        && s.DateOfJoining >= query.From
                        && s.DateOfJoining <= query.To)
            .Select(s => new { s.Branch, Joined = s.DateOfJoining!.Value, Fees = s.AdmissionFees ?? 0m })
            .ToListAsync(ct);

        /* ---- payments: fees taken in the range, branch via the student --- */
        /* The branch comes from the student, but this must NOT be a join.
           ---------------------------------------------------------------
           dbo.student holds 15 registration ids belonging to more than one
           row, so joining fees to students multiplied every fee against a
           duplicated id: 57 extra rows and 26,220 rupees of fee income that
           was never collected, inflating the branch Net by the same amount.

           A correlated lookup returns exactly one branch per fee however many
           student rows share the id. The duplicates all agree on branch, so
           which row wins does not matter - ordering by id only makes the
           choice deterministic rather than incidental.

           It also keeps a fee whose student is missing entirely: the branch
           comes back null and the row lands in "Unassigned", where an inner
           join would have dropped it and quietly understated the total. */
        var payments = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.FeeDate != null && f.FeeDate >= query.From && f.FeeDate <= query.To)
            .Select(f => new
            {
                Branch = _db.Students
                    .Where(s => s.RegistrationId == f.StudentRegistrationId)
                    .OrderBy(s => s.Id)
                    .Select(s => s.Branch)
                    .FirstOrDefault(),
                Paid = f.FeeDate!.Value,
                Amount = f.OtherInstallment ?? f.Installment ?? 0m,
                f.BillNo
            })
            .ToListAsync(ct);

        // Fines live in their own table keyed by bill number, as everywhere else.
        var billNos = payments.Select(p => p.BillNo).Where(b => b != null).Distinct().ToList();

        var fines = await _db.LateFees
            .AsNoTracking()
            .Where(l => l.BillNo != null && billNos.Contains(l.BillNo))
            .Select(l => new { l.BillNo, l.FineAmount })
            .ToListAsync(ct);

        var fineByBill = fines
            .GroupBy(f => f.BillNo!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.FineAmount ?? 0m), StringComparer.OrdinalIgnoreCase);

        /* ---- expenses: branch recorded on the expense itself ------------- */
        var expenses = await _db.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseDate != null
                        && e.ExpenseDate >= query.From
                        && e.ExpenseDate <= query.To)
            .Select(e => new
            {
                Branch = e.Ext != null ? e.Ext.Branch : null,
                Spent = e.ExpenseDate!.Value,
                Amount = e.Amount ?? 0m
            })
            .ToListAsync(ct);

        /* ---- other income: branch on the side row ------------------------ */
        var otherIncome = await _db.OtherIncomes
            .AsNoTracking()
            .Where(i => i.IncomeDate != null
                        && i.IncomeDate >= query.From
                        && i.IncomeDate <= query.To)
            .Select(i => new
            {
                Branch = i.Ext != null ? i.Ext.Branch : null,
                Earned = i.IncomeDate!.Value,
                Amount = i.Amount ?? 0m
            })
            .ToListAsync(ct);

        /* ---- receptionist pay: the branch is copied onto the payment ------
           Dated by PaidOn, when the money actually left, falling back to the
           month it was for when nobody recorded a payment date. */
        var receptionistPay = await _db.ReceptionistPayments
            .AsNoTracking()
            .Where(p => (p.PaidOn ?? p.PayMonth) >= query.From
                        && (p.PaidOn ?? p.PayMonth) <= query.To)
            .Select(p => new
            {
                p.Branch,
                Paid = p.PaidOn ?? p.PayMonth,
                p.Amount
            })
            .ToListAsync(ct);

        /* ---- stitch them together on (branch, period) -------------------- */
        var buckets = new Dictionary<(string Branch, DateOnly Start), BranchPeriodAccumulator>();

        BranchPeriodAccumulator Bucket(string? branch, DateOnly date)
        {
            var key = (BranchOf(branch), PeriodStartOf(date, grouping));

            if (!buckets.TryGetValue(key, out var acc))
            {
                acc = new BranchPeriodAccumulator();
                buckets[key] = acc;
            }

            return acc;
        }

        foreach (var a in admissions)
        {
            var acc = Bucket(a.Branch, a.Joined);
            acc.Admissions++;
            acc.AdmissionFees += a.Fees;
        }

        foreach (var p in payments)
        {
            var acc = Bucket(p.Branch, p.Paid);
            acc.Payments++;

            var fine = p.BillNo is not null && fineByBill.TryGetValue(p.BillNo, out var f) ? f : 0m;

            // Installment is stored fee + fine together, so the fee alone is the
            // difference. Never let a rounding oddity make it negative.
            acc.LateFines += fine;
            acc.FeesCollected += Math.Max(0m, p.Amount - fine);
        }

        foreach (var e in expenses)
        {
            Bucket(e.Branch, e.Spent).Expenses += e.Amount;
        }

        foreach (var i in otherIncome)
        {
            Bucket(i.Branch, i.Earned).OtherIncome += i.Amount;
        }

        foreach (var r in receptionistPay)
        {
            Bucket(r.Branch, r.Paid).ReceptionistPay += r.Amount;
        }

        var rows = buckets
            .Where(kv => wanted is null || string.Equals(kv.Key.Branch, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new BranchPeriodRowDto(
                kv.Key.Branch,
                kv.Key.Start,
                PeriodLabelOf(kv.Key.Start, grouping),
                kv.Value.Admissions,
                kv.Value.AdmissionFees,
                kv.Value.Payments,
                kv.Value.FeesCollected,
                kv.Value.LateFines,
                kv.Value.Expenses,
                kv.Value.OtherIncome,
                kv.Value.ReceptionistPay,

                // Teacher payouts are absent on purpose - see BranchReportDto.
                kv.Value.AdmissionFees + kv.Value.FeesCollected + kv.Value.LateFines
                    + kv.Value.OtherIncome
                    - kv.Value.Expenses - kv.Value.ReceptionistPay))
            .OrderBy(r => r.Branch == Unassigned)      // real branches first
            .ThenBy(r => r.Branch)
            .ThenBy(r => r.PeriodStart)
            .ToList();

        var branchTotals = rows
            .GroupBy(r => r.Branch)
            .Select(g => new BranchTotalDto(
                g.Key,
                g.Sum(r => r.Admissions),
                g.Sum(r => r.AdmissionFees),
                g.Sum(r => r.Payments),
                g.Sum(r => r.FeesCollected),
                g.Sum(r => r.LateFines),
                g.Sum(r => r.Expenses),
                g.Sum(r => r.OtherIncome),
                g.Sum(r => r.ReceptionistPay),
                g.Sum(r => r.Net)))
            .OrderBy(t => t.Branch == Unassigned)
            .ThenBy(t => t.Branch)
            .ToList();

        var grand = new BranchTotalDto(
            "All branches",
            branchTotals.Sum(t => t.Admissions),
            branchTotals.Sum(t => t.AdmissionFees),
            branchTotals.Sum(t => t.Payments),
            branchTotals.Sum(t => t.FeesCollected),
            branchTotals.Sum(t => t.LateFines),
            branchTotals.Sum(t => t.Expenses),
            branchTotals.Sum(t => t.OtherIncome),
            branchTotals.Sum(t => t.ReceptionistPay),
            branchTotals.Sum(t => t.Net));

        // Every branch on record, not just those with activity in the range, so the
        // filter does not shrink as the dates move.
        var known = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Name != null)
            .Select(b => b.Name!)
            .ToListAsync(ct);

        var branches = known
            .Concat(buckets.Keys.Select(k => k.Branch))
            .Where(b => !string.IsNullOrWhiteSpace(b) && b != Unassigned)
            .Select(b => b.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b)
            .ToList();

        // Offered only when something is actually unattributed. Listing it always
        // leaves a filter that returns nothing once every record has a branch.
        if (buckets.Keys.Any(k => k.Branch == Unassigned))
            branches.Add(Unassigned);

        // Said out loud rather than left for the reader to infer from a chart that
        // is almost entirely one bar.
        var unassignedRecords = admissions.Count(a => BranchOf(a.Branch) == Unassigned)
                                + payments.Count(p => BranchOf(p.Branch) == Unassigned)
                                + expenses.Count(e => BranchOf(e.Branch) == Unassigned);

        var totalRecords = admissions.Count + payments.Count + expenses.Count;

        return Result<BranchReportDto>.Success(new BranchReportDto(
            query.From,
            query.To,
            grouping.ToString(),
            rows,
            branchTotals,
            grand,
            branches,
            totalRecords > 0 && unassignedRecords * 2 > totalRecords,
            DateTime.Now));
    }

    private sealed class BranchPeriodAccumulator
    {
        public int Admissions;
        public decimal AdmissionFees;
        public int Payments;
        public decimal FeesCollected;
        public decimal LateFines;
        public decimal Expenses;
        public decimal OtherIncome;
        public decimal ReceptionistPay;
    }
}
