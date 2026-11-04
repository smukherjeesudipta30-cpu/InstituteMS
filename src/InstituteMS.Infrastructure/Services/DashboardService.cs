using System.Globalization;
using InstituteMS.Application.Common;
using InstituteMS.Application.Dashboard;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Common;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Aggregates the Home Dashboard in one call. Every query is parameterized LINQ —
/// no string concatenation anywhere (defect D2).
///
/// Since the finance tables were migrated (db/migrations/008) this reports the
/// institute's real position: fee collections and late fines in, institute
/// expenses and teacher payouts (<c>TecFees</c>) out.
/// </summary>
public class DashboardService : IDashboardService
{
    private const int TrendMonths = 12;

    /// <summary>How many alerts the dashboard card shows before it stops being a dashboard.</summary>
    private const int NotificationLimit = 10;

    /// <summary>
    /// How many cycles' Income reports run at once. Each is a full report (a few
    /// dozen queries), so twelve of them one after another made the dashboard —
    /// the first screen Admin sees — take 6-7 seconds; a handful side by side
    /// brings that to about one, without flooding the database with connections.
    /// </summary>
    private const int ParallelCycleReports = 4;

    private readonly AppDbContext _db;
    private readonly IIncomeService _income;
    private readonly IServiceScopeFactory _scopes;

    public DashboardService(AppDbContext db, IIncomeService income, IServiceScopeFactory scopes)
    {
        _db = db;
        _income = income;
        _scopes = scopes;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(
        DateOnly? cycleMonth = null, bool viewerIsSuperAdmin = false, CancellationToken ct = default)
    {
        /* Money is shown by PAY CYCLE (26th to 25th by default, set by Admin),
           the period teachers and receptionists are paid for, not by calendar
           month. A cycle is named by the month it ends in, as on the pay forms. */
        var startDay = await _db.PayrollSettings.AsNoTracking()
            .Select(p => (int?)p.PayCycleStartDay)
            .FirstOrDefaultAsync(ct) ?? 26;

        /* Only a COMPLETED cycle is shown: while October's cycle is running, the
           newest is September's, and October appears once it ends. A request for
           a later cycle falls back to the last completed one. */
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lastCompleted = PayCycles.LastCompletedLabelMonth(today, startDay);
        var labelMonth = cycleMonth is { } m && new DateOnly(m.Year, m.Month, 1) <= lastCompleted
            ? new DateOnly(m.Year, m.Month, 1)
            : lastCompleted;

        var counts = await GetCountsAsync(ct);

        // Every cycle the cards and the chart need, computed together up front;
        // the cards' cycle is the chart's last point, so it is computed once.
        await PrefetchCycleReportsAsync(
            Enumerable.Range(0, TrendMonths).Select(i => labelMonth.AddMonths(-i)), startDay, ct);

        var finance = await GetFinanceAsync(labelMonth, startDay, ct);
        var trend = await GetTrendAsync(labelMonth, startDay, ct);
        var enrolment = await GetCourseEnrolmentAsync(ct);
        var notices = await GetRecentNoticesAsync(ct);
        var admissions = await GetRecentAdmissionsAsync(ct);
        var payments = await GetRecentPaymentsAsync(ct);
        var enquiries = await GetPendingEnquiriesAsync(ct);
        var notifications = await GetNotificationsAsync(viewerIsSuperAdmin, ct);
        var unread = await VisibleNotifications(viewerIsSuperAdmin).CountAsync(n => n.ReadOn == null, ct);

        return new DashboardSummaryDto(
            counts, finance, trend, enrolment, notices, admissions, payments,
            enquiries, notifications, unread, DateTime.UtcNow);
    }

    /// <summary>
    /// Unread first, then newest. Capped at <see cref="NotificationLimit"/> so a
    /// long-unread dashboard stays a dashboard; the count beside it is of every
    /// unread one, not just those shown.
    /// </summary>
    private async Task<IReadOnlyList<AdminNotificationDto>> GetNotificationsAsync(bool viewerIsSuperAdmin, CancellationToken ct) =>
        await VisibleNotifications(viewerIsSuperAdmin)
            .AsNoTracking()
            .OrderBy(n => n.ReadOn == null ? 0 : 1)
            .ThenByDescending(n => n.CreatedOn)
            .ThenByDescending(n => n.Id)
            .Take(NotificationLimit)
            .Select(n => new AdminNotificationDto(
                n.Id, n.Type, n.Title, n.StudentName, n.CourseName,
                n.RegistrationId, n.LoginId, n.DetailValue, n.CreatedOn,
                n.ReadOn != null))
            .ToListAsync(ct);

    /// <summary>
    /// Password-change alerts carry the new password, so an alert about anyone who
    /// can reach this dashboard — SuperAdmin, Admin or Editor — is SuperAdmin's
    /// alone. Hiding only an Editor's left the others open: an Editor could read a
    /// new Admin or SuperAdmin password here and sign in as them. Receptionist,
    /// Teacher and Student alerts stay visible to Admin and Editor as before.
    /// </summary>
    private static readonly string[] SuperAdminOnlyPasswordRoles =
        [UserRoles.SuperAdmin, UserRoles.Admin, UserRoles.Editor];

    /// <summary>
    /// The alerts this viewer may see: an Admin or Editor neither sees a
    /// SuperAdmin-only alert, counts it in the badge, nor can mark it read. The
    /// alert records the person's role in CourseName (see AuthService), which is
    /// what this reads — so existing alerts are covered too.
    /// </summary>
    /// <remarks>PreSchool alerts are shown on the PreSchool's own dashboard, never on this one.</remarks>
    private IQueryable<Domain.Entities.AdminNotification> VisibleNotifications(bool viewerIsSuperAdmin)
    {
        var computerCentre = _db.AdminNotifications
            .Where(n => n.Type != Domain.Entities.AdminNotificationTypes.PreSchoolPasswordChange);
        return viewerIsSuperAdmin
            ? computerCentre
            : computerCentre.Where(n => !(n.Type == Domain.Entities.AdminNotificationTypes.StaffPasswordChange
                                          && SuperAdminOnlyPasswordRoles.Contains(n.CourseName)));
    }

    public async Task<Result<bool>> MarkNotificationReadAsync(
        int id, string? actor, bool viewerIsSuperAdmin = false, CancellationToken ct = default)
    {
        var notification = await VisibleNotifications(viewerIsSuperAdmin).FirstOrDefaultAsync(n => n.Id == id, ct);

        if (notification is null)
            return Result<bool>.Failure("Notification not found.", ResultStatus.NotFound);

        // Already read is success, not an error: two tabs open is not a failure.
        if (notification.ReadOn is null)
        {
            notification.ReadOn = DateTime.Now;
            notification.ReadBy = actor;
            await _db.SaveChangesAsync(ct);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> MarkAllNotificationsReadAsync(
        string? actor, bool viewerIsSuperAdmin = false, CancellationToken ct = default)
    {
        var unread = await VisibleNotifications(viewerIsSuperAdmin).Where(n => n.ReadOn == null).ToListAsync(ct);

        foreach (var notification in unread)
        {
            notification.ReadOn = DateTime.Now;
            notification.ReadBy = actor;
        }

        if (unread.Count > 0) await _db.SaveChangesAsync(ct);

        return Result<int>.Success(unread.Count);
    }

    private async Task<DashboardCountsDto> GetCountsAsync(CancellationToken ct)
    {
        var activeStudents = await _db.Students.CountAsync(s => s.IsActive == true, ct);
        // "!s.IsActive == true" would be (!IsActive) == true, which counts a NULL
        // student as neither active nor inactive. "!= true" puts NULL here, where
        // an unknown status belongs.
        var inactiveStudents = await _db.Students.CountAsync(s => s.IsActive != true, ct);
        var completedStudents = await _db.Students.CountAsync(s => s.IsComplete == true, ct);
        var courses = await _db.Courses.CountAsync(ct);

        // Placeholder teachers were an artefact of migration 004, which only ever
        // ran against the migrated database. dbo.Teacher in the legacy database has
        // no CreatedBy column to mark them with and holds no such rows, so every
        // teacher here is a real one.
        var teachers = await _db.Teachers.CountAsync(ct);

        var studentAccounts = await _db.Users
            .CountAsync(u => u.IsActive && u.Roles != null && u.Roles.Contains(UserRoles.Student), ct);

        var staffAccounts = await _db.Users
            .CountAsync(u => u.IsActive && (u.Roles == null || !u.Roles.Contains(UserRoles.Student)), ct);

        // No side row means nobody has dealt with it yet, which is exactly the
        // enquiry that should still be counted.
        var enquiries = await _db.ContactMessages
            .CountAsync(m => m.Ext == null || !m.Ext.IsHandled, ct);

        return new DashboardCountsDto(
            activeStudents, inactiveStudents, completedStudents,
            courses, teachers, staffAccounts, studentAccounts, enquiries);
    }

    /// <summary>First and last day of the cycle named by <paramref name="labelMonth"/>.</summary>
    private static (DateOnly Start, DateOnly End) CycleRange(DateOnly labelMonth, int startDay) =>
        PayCycles.Range(labelMonth, startDay);

    /// <summary>
    /// The Income report for one pay cycle, with the proprietor's honorarium
    /// counted once. The report charges that fixed monthly amount in full for
    /// every CALENDAR month a range touches, and a 26th-to-25th cycle touches two;
    /// a cycle is one month's pay, so it carries the amount of the month it is
    /// named after, once.
    /// </summary>
    private async Task<IncomeReportDto> CycleReportAsync(DateOnly labelMonth, int startDay, CancellationToken ct)
    {
        if (_cycleReports.TryGetValue(labelMonth, out var cached)) return cached;

        _honorariumRates ??= await _db.ProprietorHonoraria.AsNoTracking().ToListAsync(ct);
        return _cycleReports[labelMonth] = await BuildCycleReportAsync(_income, _honorariumRates, labelMonth, startDay, ct);
    }

    /// <summary>
    /// Runs the given cycles' reports side by side, a few at a time, each on its
    /// own scope — a DbContext cannot serve two queries at once — and keeps them
    /// for <see cref="CycleReportAsync"/>. Same figures as running them in turn.
    /// </summary>
    private async Task PrefetchCycleReportsAsync(IEnumerable<DateOnly> cycles, int startDay, CancellationToken ct)
    {
        _honorariumRates ??= await _db.ProprietorHonoraria.AsNoTracking().ToListAsync(ct);
        var rates = _honorariumRates;

        using var gate = new SemaphoreSlim(ParallelCycleReports);
        var work = cycles.Distinct().Where(c => !_cycleReports.ContainsKey(c)).Select(async cycle =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var scope = _scopes.CreateScope();
                var income = scope.ServiceProvider.GetRequiredService<IIncomeService>();
                return (cycle, report: await BuildCycleReportAsync(income, rates, cycle, startDay, ct));
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        foreach (var (cycle, report) in await Task.WhenAll(work))
            _cycleReports[cycle] = report;
    }

    private static async Task<IncomeReportDto> BuildCycleReportAsync(
        IIncomeService income, List<Domain.Entities.ProprietorHonorarium> rates,
        DateOnly labelMonth, int startDay, CancellationToken ct)
    {
        var (start, end) = CycleRange(labelMonth, startDay);
        var report = (await income.GetReportAsync(start, end, null, ct)).Value!;

        // The Income report now charges each cycle's honorarium exactly once itself,
        // so this is a safeguard that finds nothing to correct.
        var once = HonorariumService.AllocateByBranch(rates, start, end, startDay).Sum(m => m.Amount);
        var excess = report.ProprietorHonorarium - once;
        if (excess == 0m) return report;

        return report with
        {
            ProprietorHonorarium = once,
            TotalCost = report.TotalCost - excess,
            NetProfit = report.NetProfit + excess
        };
    }

    /// <summary>Loaded once per request: every cycle of the trend reads the same list.</summary>
    private List<Domain.Entities.ProprietorHonorarium>? _honorariumRates;

    /// <summary>This request's cycle reports, by the month each cycle is named after.</summary>
    private readonly Dictionary<DateOnly, IncomeReportDto> _cycleReports = new();

    /// <summary>
    /// The cycle's money, taken from the Income report itself rather than a second
    /// calculation, so the two always show the same figures: fees, admission and
    /// exam fees and other income in; the teachers' share (counted once), expenses,
    /// receptionist pay and every other cost out.
    /// </summary>
    private async Task<DashboardFinanceDto> GetFinanceAsync(DateOnly labelMonth, int startDay, CancellationToken ct)
    {
        var (start, end) = CycleRange(labelMonth, startDay);
        var report = await CycleReportAsync(labelMonth, startDay, ct);

        var examFees = report.Sections.FirstOrDefault(s => s.Section == IncomeSection.ExamFees)?.Amount ?? 0m;
        var otherCosts = report.TotalCost - report.Expenses - report.TeacherPayouts - report.ReceptionistPay;

        var (outstanding, inArrears) = await GetOutstandingAsync(ct);

        var lastPaymentOn = await _db.MonthlyFees
            .Select(f => f.PaymentDate ?? f.FeeDate)
            .MaxAsync(ct);

        return new DashboardFinanceDto(
            report.FeeCollections, report.LateFines, report.OtherIncome, report.Expenses, report.TeacherPayouts,
            report.TotalIncome, report.TotalCost, report.NetProfit,
            outstanding, inArrears, lastPaymentOn,
            start, end, labelMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            report.AdmissionFees, examFees, report.ReceptionistPay, otherCosts, startDay);
    }

    /// <summary>
    /// Outstanding dues = the latest <c>NewDue</c> recorded against each active
    /// student. Negative values are ignored: the legacy ledger sometimes carries a
    /// credit forward, which is not money owed to the institute.
    /// </summary>
    private async Task<(decimal Outstanding, int StudentsInArrears)> GetOutstandingAsync(CancellationToken ct)
    {
        var activeRegIds = _db.Students
            .Where(s => s.IsActive == true && s.RegistrationId != null)
            .Select(s => s.RegistrationId!);

        var latestDues = await _db.MonthlyFees
            .Where(f => f.StudentRegistrationId != null && activeRegIds.Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => g.OrderByDescending(f => f.FeeDate).ThenByDescending(f => f.Id)
                          .Select(f => f.NewDue).FirstOrDefault())
            .ToListAsync(ct);

        var owed = latestDues.Where(d => d is > 0m).Select(d => d!.Value).ToList();

        return (owed.Sum(), owed.Count);
    }

    /// <summary>
    /// The twelve pay cycles ending with the chosen one, each read from the Income
    /// report like the cards above, so the chart and the cards never disagree.
    /// </summary>
    private async Task<IReadOnlyList<MonthlyTrendPointDto>> GetTrendAsync(
        DateOnly labelMonth, int startDay, CancellationToken ct)
    {
        var points = new List<MonthlyTrendPointDto>(TrendMonths);

        for (var i = TrendMonths - 1; i >= 0; i--)
        {
            var cycle = labelMonth.AddMonths(-i);
            var (start, end) = CycleRange(cycle, startDay);
            var report = await CycleReportAsync(cycle, startDay, ct);

            var label = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(cycle.Month);
            points.Add(new MonthlyTrendPointDto(
                cycle.Year, cycle.Month, label,
                report.TotalIncome, report.TotalCost - report.TeacherPayouts, report.TeacherPayouts));
        }

        return points;
    }

    private async Task<IReadOnlyList<CourseEnrolmentDto>> GetCourseEnrolmentAsync(CancellationToken ct)
    {
        // Ordering happens on the anonymous projection — EF cannot sort by a
        // member of a constructor-projected record, so the DTO is built after.
        var rows = await _db.Courses
            .AsNoTracking()
            .Select(c => new
            {
                c.Cid,
                c.Name,
                StudentCount = c.Students.Count(s => s.IsActive == true)
            })
            .Where(c => c.StudentCount > 0)
            .OrderByDescending(c => c.StudentCount)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return rows
            .Select(r => new CourseEnrolmentDto(r.Cid, r.Name ?? "(unnamed)", r.StudentCount))
            .ToList();
    }

    private async Task<IReadOnlyList<NoticeDto>> GetRecentNoticesAsync(CancellationToken ct) =>
        await _db.Notices
            .AsNoTracking()
            .OrderByDescending(n => n.NoticeDate)
            .Take(5)
            .Select(n => new NoticeDto(n.Id, n.Text ?? string.Empty, n.NoticeDate))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<RecentStudentDto>> GetRecentAdmissionsAsync(CancellationToken ct) =>
        await _db.Students
            .AsNoTracking()
            .Where(s => s.IsActive == true)
            .OrderByDescending(s => s.DateOfJoining)
            .ThenByDescending(s => s.Id)
            .Take(5)
            .Select(s => new RecentStudentDto(
                s.Id, s.Name, s.RegistrationId, s.Course != null ? s.Course.Name : null, s.DateOfJoining))
            .ToListAsync(ct);

    /// <summary>
    /// Latest fee collections. The student name comes from a join on
    /// <c>StuRegId</c>, and because the legacy data has duplicate registration ids
    /// the first match is taken rather than assuming one.
    /// </summary>
    public async Task<Result<bool>> MarkEnquiryHandledAsync(
        int id, bool handled, CancellationToken ct = default)
    {
        var enquiry = await _db.ContactMessages
            .Include(m => m.Ext)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (enquiry is null)
            return Result<bool>.Failure("That enquiry no longer exists.", ResultStatus.NotFound);

        /* The flag has to be written to the side table.

           dbo.CusMessage cannot gain a column — the legacy application inserts into
           it positionally — so ContactMessage.IsHandled is deliberately NOT mapped,
           and setting it wrote nothing at all. SaveChanges then reported success
           over an empty change set, so every "mark handled" since this shipped
           appeared to work and did nothing: the same enquiries from 2019 came back
           on the next page load.

           Marked, never deleted — the enquiry stays a record of who got in touch,
           and GetEnquiriesAsync finds it again. */
        if (enquiry.Ext is null)
        {
            _db.ContactMessageExts.Add(new InstituteMS.Domain.Entities.ContactMessageExt
            {
                MessageId = id,
                IsHandled = handled,
                HandledOn = handled ? DateTime.Now : null
            });
        }
        else
        {
            enquiry.Ext.IsHandled = handled;
            enquiry.Ext.HandledOn = handled ? DateTime.Now : null;
        }

        // Kept in step so anything still reading the unmapped property agrees.
        enquiry.IsHandled = handled;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Enquiries from the public contact form, filtered by whether they have been
    /// read.
    /// </summary>
    /// <remarks>
    /// The dashboard panel deliberately shows only the unread ones, so that it is a
    /// list of work outstanding rather than an archive. This is the other half of
    /// that bargain: without it, marking a message read put it permanently out of
    /// reach, which is not what "read" should mean.
    /// </remarks>
    public async Task<EnquiryListDto> GetEnquiriesAsync(
        EnquiryFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _db.ContactMessages.AsNoTracking();

        // Ext is null for every message that predates the coexistence script, and an
        // absent row means nobody has marked it — so null counts as unread.
        var newCount = await all.CountAsync(m => m.Ext == null || !m.Ext.IsHandled, ct);
        var readCount = await all.CountAsync(m => m.Ext != null && m.Ext.IsHandled, ct);

        var q = filter switch
        {
            EnquiryFilter.New => all.Where(m => m.Ext == null || !m.Ext.IsHandled),
            EnquiryFilter.Read => all.Where(m => m.Ext != null && m.Ext.IsHandled),
            _ => all
        };

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(m => m.SubmittedOn)
            .ThenByDescending(m => m.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new EnquiryDto(
                m.Id, m.Name, m.Email, m.Phone, m.Body, m.SubmittedOn,
                m.Ext != null && m.Ext.IsHandled))
            .ToListAsync(ct);

        return new EnquiryListDto(items, totalCount, newCount, readCount);
    }

    /// <summary>
    /// The unhandled enquiries themselves, newest first. The dashboard has always
    /// counted these, but there was nothing anywhere that showed the messages - the
    /// badge said "15 new enquiry" and that was the end of it.
    /// </summary>
    /// <remarks>
    /// Capped so one spam run cannot make the dashboard payload enormous; the count
    /// beside it is the true total, so a larger backlog is still visible as a number.
    /// </remarks>
    private async Task<IReadOnlyList<EnquiryDto>> GetPendingEnquiriesAsync(CancellationToken ct)
    {
        return await _db.ContactMessages
            .AsNoTracking()
            .Where(m => m.Ext == null || !m.Ext.IsHandled)
            .OrderByDescending(m => m.SubmittedOn)
            .ThenByDescending(m => m.Id)
            .Take(25)
            .Select(m => new EnquiryDto(
                m.Id, m.Name, m.Email, m.Phone, m.Body, m.SubmittedOn, false))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<RecentPaymentDto>> GetRecentPaymentsAsync(CancellationToken ct)
    {
        var rows = await _db.MonthlyFees
            .AsNoTracking()
            .OrderByDescending(f => f.PaymentDate ?? f.FeeDate)
            .ThenByDescending(f => f.Id)
            .Take(5)
            .Select(f => new
            {
                f.Id,
                f.StudentRegistrationId,
                f.BillNo,
                f.Installment,
                PaidOn = f.PaymentDate ?? f.FeeDate,
                StudentName = _db.Students
                    .Where(s => s.RegistrationId == f.StudentRegistrationId)
                    .Select(s => s.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new RecentPaymentDto(
                r.Id, r.StudentName, r.StudentRegistrationId, r.BillNo, r.Installment, r.PaidOn))
            .ToList();
    }
}
