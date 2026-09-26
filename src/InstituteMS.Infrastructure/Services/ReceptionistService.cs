using System.Globalization;
using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The receptionist master and their monthly pay.
/// </summary>
/// <remarks>
/// Modelled on the teacher master so the screens read alike, with two deliberate
/// differences. A receptionist is paid a <b>monthly</b> amount rather than a share
/// of each fee, so a payment names the month it is for and there is one per month.
/// And a receptionist belongs to a <b>branch</b>, which is what makes the report
/// branch-wise.
/// </remarks>
public class ReceptionistService : IReceptionistService
{
    private readonly AppDbContext _db;
    private readonly IChatService _chat;

    public ReceptionistService(AppDbContext db, IChatService chat)
    {
        _db = db;
        _chat = chat;
    }

    /// <summary>Any date inside a month, reduced to the first of it.</summary>
    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    /* ------------------------------------------------------------- master */

    public async Task<PagedResult<ReceptionistListItemDto>> GetListAsync(
        ReceptionistQuery query, CancellationToken ct = default)
    {
        var q = _db.Receptionists.AsNoTracking();

        if (query.ActiveOnly)
            q = q.Where(r => r.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(r => r.Branch == branch);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(r => EF.Functions.Like(r.Name, $"%{term}%")
                             || (r.LoginId != null && EF.Functions.Like(r.LoginId, $"%{term}%"))
                             || (r.Phone != null && EF.Functions.Like(r.Phone, $"%{term}%")));
        }

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new ReceptionistListItemDto(
                r.Id, r.Name, r.LoginId, r.Phone, r.PersonalEmail, r.Branch, r.IsActive,
                r.Designation, r.Qualification, r.ComputerTraining, r.IsTechnical,
                r.MonthlyPay,
                r.Payments.Count,
                r.Payments.Sum(p => (decimal?)(p.Amount + p.ExtraShiftBonus + p.ReferralBonus)) ?? 0m,
                r.Payments.Max(p => (DateOnly?)p.PayMonth)))
            .ToListAsync(ct);

        return PagedResult<ReceptionistListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Checks the branch is one on record. The column is free text, so a typo would
    /// create a phantom branch that quietly splits the report.
    /// </summary>
    private async Task<Result<string>> ResolveBranchAsync(string? branch, CancellationToken ct)
    {
        var name = branch?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Result<string>.Failure("Choose the branch this receptionist works at.");

        var known = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Name != null && b.Name.ToLower() == name.ToLower())
            .Select(b => b.Name!)
            .FirstOrDefaultAsync(ct);

        return known is null
            ? Result<string>.Failure($"'{name}' is not a branch on record.")
            : Result<string>.Success(known);
    }

    private static string? RecBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<Result<int>> CreateAsync(
        ReceptionistSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<int>.Failure(branch.Error!, branch.Status);

        var loginId = string.IsNullOrWhiteSpace(request.LoginId) ? null : request.LoginId.Trim();

        if (loginId is not null && await _db.Receptionists.AnyAsync(r => r.LoginId == loginId, ct))
            return Result<int>.Failure("Another receptionist already uses that user id.", ResultStatus.Conflict);

        var person = new Receptionist
        {
            Name = request.Name!.Trim(),
            LoginId = loginId,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Branch = branch.Value,
            IsActive = request.IsActive,
            PersonalEmail = string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            Designation = RecBlank(request.Designation),
            Qualification = RecBlank(request.Qualification),
            ComputerTraining = RecBlank(request.ComputerTraining),
            IsTechnical = request.IsTechnical,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.Receptionists.Add(person);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(person.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, ReceptionistSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var person = await _db.Receptionists.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (person is null)
            return Result<bool>.Failure("That receptionist no longer exists.", ResultStatus.NotFound);

        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<bool>.Failure(branch.Error!, branch.Status);

        var loginId = string.IsNullOrWhiteSpace(request.LoginId) ? null : request.LoginId.Trim();

        if (loginId is not null
            && await _db.Receptionists.AnyAsync(r => r.LoginId == loginId && r.Id != id, ct))
        {
            return Result<bool>.Failure("Another receptionist already uses that user id.", ResultStatus.Conflict);
        }

        person.Name = request.Name!.Trim();
        person.LoginId = loginId;
        person.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        person.PersonalEmail = string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim();
        person.Designation = RecBlank(request.Designation);
        person.Qualification = RecBlank(request.Qualification);
        person.ComputerTraining = RecBlank(request.ComputerTraining);
        person.IsTechnical = request.IsTechnical;
        person.Branch = branch.Value;
        person.IsActive = request.IsActive;
        person.ModifiedBy = actor;
        person.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var person = await _db.Receptionists.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (person is null)
            return Result<bool>.Failure("That receptionist no longer exists.", ResultStatus.NotFound);

        // Payments are the record of money that left the institute, so a person who
        // has been paid is never removed — mark them inactive instead.
        var payments = await _db.ReceptionistPayments.CountAsync(p => p.ReceptionistId == id, ct);

        if (payments > 0)
        {
            return Result<bool>.Failure(
                $"{person.Name} has {payments} payment(s) recorded, so the record cannot be " +
                "deleted. Mark them inactive instead.",
                ResultStatus.Conflict);
        }

        _db.Receptionists.Remove(person);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UpdateMonthlyPayAsync(
        int id, decimal monthlyPay, string? actor, CancellationToken ct = default)
    {
        var person = await _db.Receptionists.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (person is null)
            return Result<bool>.Failure("That receptionist no longer exists.", ResultStatus.NotFound);

        person.MonthlyPay = monthlyPay;
        person.ModifiedBy = actor;
        person.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ----------------------------------------------------------- payments */

    public async Task<Result<int>> RecordPaymentAsync(
        ReceptionistPaymentRequest request, string? actor, CancellationToken ct = default)
    {
        var person = await _db.Receptionists
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ReceptionistId, ct);

        if (person is null)
            return Result<int>.Failure("That receptionist no longer exists.", ResultStatus.NotFound);

        var month = MonthStart(request.PayMonth);

        // Computed here, never taken from the request: the bonus is the server's
        // own read of who covered which shift, not a figure anyone types in. See
        // GetExtraShiftBonusAsync for how it is worked out.
        var bonus = await GetExtraShiftBonusAsync(request.ReceptionistId, request.PayMonth, ct);
        var extraShiftBonus = bonus.Value?.NetBonus ?? 0m;

        // One payment per person per month. A second for the same month is a
        // correction to the first, so it updates rather than adding a second salary.
        var existing = await _db.ReceptionistPayments
            .FirstOrDefaultAsync(p => p.ReceptionistId == request.ReceptionistId && p.PayMonth == month, ct);

        if (existing is not null)
        {
            // Only announce the transfer when it actually changed — re-saving a
            // correction to the amount or notes must not repeat an identical
            // broadcast every time the same month's payment is reopened.
            var bonusChanged = existing.ExtraShiftBonus != extraShiftBonus;

            existing.Amount = request.Amount;
            existing.ExtraShiftBonus = extraShiftBonus;
            existing.PaidOn = request.PaidOn;
            existing.Notes = request.Notes?.Trim();
            // The branch is refreshed from the person, so correcting a payment after
            // they move branch puts it where they are now.
            existing.Branch = person.Branch;

            await _db.SaveChangesAsync(ct);
            await AttachReferralCreditsAsync(existing, ct);

            if (bonusChanged && bonus.Value is not null)
                await NotifyExtraShiftBonusAsync(person.Name, person.Branch, month, bonus.Value, actor, ct);

            return Result<int>.Success(existing.Id);
        }

        var payment = new ReceptionistPayment
        {
            ReceptionistId = request.ReceptionistId,
            PayMonth = month,
            PaidOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Now),
            Amount = request.Amount,
            ExtraShiftBonus = extraShiftBonus,
            // Copied, not joined, so a later branch move does not rewrite history.
            Branch = person.Branch,
            Notes = request.Notes?.Trim(),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.ReceptionistPayments.Add(payment);
        await _db.SaveChangesAsync(ct);
        await AttachReferralCreditsAsync(payment, ct);

        if (bonus.Value is not null)
            await NotifyExtraShiftBonusAsync(person.Name, person.Branch, month, bonus.Value, actor, ct);

        return Result<int>.Success(payment.Id);
    }

    /// <summary>
    /// Pays the receptionist's admission referral credits on this payment: every
    /// credit not yet paid, earned up to the end of the CALENDAR month this
    /// payment is for (an admission any day in September is paid with
    /// September's pay - unlike the extra-shift adjustment, which follows the
    /// 26th-to-25th pay cycle). Credits already on this payment stay on it, so
    /// re-saving a correction never pays them twice or drops them.
    /// </summary>
    private async Task AttachReferralCreditsAsync(ReceptionistPayment payment, CancellationToken ct)
    {
        var cycleEnd = CalendarMonthEnd(payment.PayMonth);

        var unpaid = await _db.ReferralCredits
            .Where(r => r.ReferrerType == ReferrerTypes.Receptionist
                        && r.ReferrerId == payment.ReceptionistId
                        && r.ReceptionistPaymentId == null
                        && r.CreditedOn <= cycleEnd)
            .ToListAsync(ct);

        foreach (var credit in unpaid) credit.ReceptionistPaymentId = payment.Id;
        await _db.SaveChangesAsync(ct);

        payment.ReferralBonus = await _db.ReferralCredits
            .Where(r => r.ReceptionistPaymentId == payment.Id)
            .SumAsync(r => (decimal?)r.Amount ?? 0m, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static DateOnly CalendarMonthEnd(DateOnly anyDayInMonth) =>
        new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1).AddMonths(1).AddDays(-1);

    public async Task<Result<decimal>> GetPendingReferralBonusAsync(
        int receptionistId, DateOnly forMonth, CancellationToken ct = default)
    {
        var month = MonthStart(forMonth);
        var cycleEnd = CalendarMonthEnd(month);

        var existingId = await _db.ReceptionistPayments.AsNoTracking()
            .Where(p => p.ReceptionistId == receptionistId && p.PayMonth == month)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);

        var amount = await _db.ReferralCredits.AsNoTracking()
            .Where(r => r.ReferrerType == ReferrerTypes.Receptionist && r.ReferrerId == receptionistId
                        && ((r.ReceptionistPaymentId == null && r.CreditedOn <= cycleEnd)
                            || (existingId != null && r.ReceptionistPaymentId == existingId)))
            .SumAsync(r => (decimal?)r.Amount ?? 0m, ct);

        return Result<decimal>.Success(amount);
    }

    public async Task<Result<IReadOnlyList<ReceptionistPaymentDto>>> GetPaymentsAsync(
        int receptionistId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var q = _db.ReceptionistPayments
            .AsNoTracking()
            .Where(p => p.ReceptionistId == receptionistId);

        if (from.HasValue) q = q.Where(p => p.PayMonth >= MonthStart(from.Value));
        if (to.HasValue) q = q.Where(p => p.PayMonth <= to.Value);

        var rows = await q
            .OrderByDescending(p => p.PayMonth)
            .Select(p => new ReceptionistPaymentDto(
                p.Id, p.ReceptionistId,
                p.Receptionist != null ? p.Receptionist.Name : null,
                p.PayMonth, p.PaidOn, p.Amount, p.ExtraShiftBonus, p.Branch, p.Notes,
                p.CreatedBy, p.CreatedOn, p.ReferralBonus))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ReceptionistPaymentDto>>.Success(rows);
    }

    public async Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.ReceptionistPayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null)
            return Result<bool>.Failure("That payment no longer exists.", ResultStatus.NotFound);

        // Referral credits paid on it go back to unpaid, to be paid on the next one.
        var credits = await _db.ReferralCredits.Where(r => r.ReceptionistPaymentId == paymentId).ToListAsync(ct);
        foreach (var credit in credits) credit.ReceptionistPaymentId = null;

        _db.ReceptionistPayments.Remove(payment);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ------------------------------------------------------------- report */

    public async Task<Result<ReceptionistReportDto>> GetReportAsync(
        ReceptionistReportQuery query, CancellationToken ct = default)
    {
        if (query.To < query.From)
            return Result<ReceptionistReportDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var fromMonth = MonthStart(query.From);

        var q = _db.ReceptionistPayments
            .AsNoTracking()
            .Where(p => p.PayMonth >= fromMonth && p.PayMonth <= query.To);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(p => p.Branch == branch);
        }

        var rows = await q
            .Select(p => new
            {
                Branch = p.Branch ?? "Unassigned",
                p.PayMonth,
                p.ReceptionistId,
                Name = p.Receptionist != null ? p.Receptionist.Name : "(removed)",
                Amount = p.Amount + p.ExtraShiftBonus + p.ReferralBonus
            })
            .ToListAsync(ct);

        var reportRows = rows
            .Select(r => new ReceptionistReportRowDto(
                r.Branch,
                r.PayMonth,
                r.PayMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                r.ReceptionistId,
                r.Name,
                r.Amount))
            .OrderBy(r => r.Branch)
            .ThenBy(r => r.PeriodStart)
            .ThenBy(r => r.ReceptionistName)
            .ToList();

        var branchTotals = reportRows
            .GroupBy(r => r.Branch)
            .Select(g => new ReceptionistBranchTotalDto(
                g.Key,
                g.Select(r => r.ReceptionistId).Distinct().Count(),
                g.Count(),
                g.Sum(r => r.Amount)))
            .OrderBy(t => t.Branch)
            .ToList();

        // Every branch a receptionist is attached to, so the filter does not shrink
        // as the dates move.
        var branches = await _db.Receptionists
            .AsNoTracking()
            .Where(r => r.Branch != null)
            .Select(r => r.Branch!)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync(ct);

        return Result<ReceptionistReportDto>.Success(new ReceptionistReportDto(
            query.From, query.To,
            reportRows, branchTotals,
            branchTotals.Sum(t => t.TotalPaid),
            branches,
            DateTime.Now));
    }

    /* ------------------------------------------------------- leave & deduction */

    private static (DateOnly Start, DateOnly End) PeriodRange(string periodType, DateOnly date) => periodType switch
    {
        "Weekly" => WeekRange(date),
        "Yearly" => (new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31)),
        _ => (MonthStart(date), new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)))
    };

    /// <summary>The average length of a month, for prorating a monthly figure across an arbitrary range.</summary>
    private const decimal DaysPerMonth = 30.44m;

    /// <summary>
    /// The pay cycle containing <paramref name="anchor"/>, per the institute's
    /// configured start day (26 by default: the 26th of one month to the 25th of
    /// the next) — <b>not</b> the calendar month. Used for the extra-shift bonus,
    /// which is a pay-period concept; the leave report above stays calendar-month,
    /// a separate and already-established reading of "monthly".
    /// </summary>
    private static (DateOnly Start, DateOnly End) PayCycleRange(DateOnly anchor, int startDay)
    {
        // Clamped per month so a start day near month-end (up to 28, enforced by
        // the settings validator) never overflows a short month like February.
        int Clamp(int year, int month) => Math.Min(startDay, DateTime.DaysInMonth(year, month));

        DateOnly start;
        if (anchor.Day >= Clamp(anchor.Year, anchor.Month))
        {
            start = new DateOnly(anchor.Year, anchor.Month, Clamp(anchor.Year, anchor.Month));
        }
        else
        {
            var prevMonth = anchor.AddMonths(-1);
            start = new DateOnly(prevMonth.Year, prevMonth.Month, Clamp(prevMonth.Year, prevMonth.Month));
        }

        var nextMonth = start.AddMonths(1);
        var end = nextMonth.AddDays(-1);
        return (start, end);
    }

    /// <summary>The institute-wide pay cycle start day — 26 unless an Admin has changed it.</summary>
    private async Task<int> GetPayCycleStartDayAsync(CancellationToken ct)
    {
        var day = await _db.PayrollSettings.AsNoTracking()
            .Select(p => (int?)p.PayCycleStartDay)
            .FirstOrDefaultAsync(ct);

        return day is > 0 and <= 28 ? day.Value : 26;
    }

    /// <summary>Monday-start week containing the given date.</summary>
    private static (DateOnly Start, DateOnly End) WeekRange(DateOnly date)
    {
        var dow = (int)date.DayOfWeek; // System.DayOfWeek: 0 = Sunday
        var offsetToMonday = dow == 0 ? 6 : dow - 1;
        var monday = date.AddDays(-offsetToMonday);
        return (monday, monday.AddDays(6));
    }

    /// <summary>
    /// Scales the monthly entitlement in <see cref="ReceptionistLeavePolicy"/> to
    /// whatever period the report was asked for, on a straight months-per-year
    /// basis — the policy only ever states a monthly figure.
    /// </summary>
    private static decimal EntitlementForPeriod(string periodType, int shiftsPerWeek, DateOnly start, DateOnly end)
    {
        var monthly = ReceptionistLeavePolicy.MonthlyEntitlementDays(shiftsPerWeek);
        return periodType switch
        {
            "Weekly" => Math.Round(monthly * 12m / 52m, 2),
            "Yearly" => monthly * 12m,
            // A hand-picked range is prorated by how many days it spans, so a
            // fortnight earns about half a month's leave and a full month earns
            // a month's.
            "Custom" => Math.Round(monthly * (end.DayNumber - start.DayNumber + 1) / DaysPerMonth, 2),
            _ => monthly
        };
    }

    /// <summary>
    /// Leave taken against entitlement for every active, rostered receptionist in
    /// one period. Taken days are never stored (same rule as attendance itself —
    /// see <see cref="Domain.Entities.StaffAttendance"/>): they are the rostered
    /// days in the period, from <see cref="StaffShiftAssignment"/>, minus the days
    /// actually scanned in.
    /// </summary>
    public async Task<Result<ReceptionistLeaveReportDto>> GetLeaveReportAsync(
        ReceptionistLeaveReportQuery query, CancellationToken ct = default)
    {
        var periodType = query.PeriodType is "Weekly" or "Yearly" or "Custom" ? query.PeriodType : "Monthly";
        var anchor = query.Date == default ? DateOnly.FromDateTime(DateTime.Today) : query.Date;

        DateOnly start, end;
        if (periodType == "Custom")
        {
            // A hand-picked range; fall back to the month around the anchor if
            // one end is missing, and swap if they came in reversed.
            start = query.From ?? MonthStart(anchor);
            end = query.To ?? new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month));
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            (start, end) = PeriodRange(periodType, anchor);
        }

        // A day that hasn't happened yet has no scan and no roster outcome either
        // way — counting it as "leave" would flag every receptionist as over
        // entitlement the moment a monthly or yearly report is run mid-period.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var countEnd = end > today ? today : end;

        var receptionistsQuery = _db.Receptionists.AsNoTracking().Where(r => r.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            receptionistsQuery = receptionistsQuery.Where(r => r.Branch == branch);
        }

        var receptionists = await receptionistsQuery
            .Select(r => new { r.Id, r.Name, r.Branch })
            .ToListAsync(ct);

        var receptionistIds = receptionists.Select(r => r.Id).ToList();

        var assignments = await _db.StaffShiftAssignments.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.IsActive && receptionistIds.Contains(a.StaffId))
            .Select(a => new { a.StaffId, a.DayOfWeek })
            .ToListAsync(ct);

        // Two different views of the same rota. "Shifts per week" counts every
        // shift assigned, so a day worked morning and evening counts twice — it
        // is what the entitlement tier is chosen by. Leave, though, is whether
        // the person came in on a rostered day at all, so it is measured against
        // the distinct days: a day off is one day of leave however many shifts
        // it held.
        var shiftCountByReceptionist = assignments
            .GroupBy(a => a.StaffId)
            .ToDictionary(g => g.Key, g => g.Count());

        var rosteredDaysByReceptionist = assignments
            .GroupBy(a => a.StaffId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.DayOfWeek).Distinct().ToHashSet());

        // When countEnd < start (the whole period is still in the future),
        // this legitimately returns no rows rather than needing a separate branch.
        var presentDays = await _db.StaffAttendance.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.Direction == "In"
                && a.OnDate >= start && a.OnDate <= countEnd && receptionistIds.Contains(a.StaffId))
            .Select(a => new { a.StaffId, a.OnDate })
            .Distinct()
            .ToListAsync(ct);

        var presentByReceptionist = presentDays
            .GroupBy(p => p.StaffId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.OnDate).ToHashSet());

        var existingDecisions = await _db.ReceptionistLeaveDeductions.AsNoTracking()
            .Where(d => d.PeriodType == periodType && d.PeriodStart == start && receptionistIds.Contains(d.ReceptionistId))
            .ToDictionaryAsync(d => d.ReceptionistId, ct);

        // Nothing to count yet when the whole period is still in the future.
        var calendarDayCount = countEnd < start ? 0 : countEnd.DayNumber - start.DayNumber + 1;
        var calendarDays = Enumerable.Range(0, calendarDayCount)
            .Select(offset => start.AddDays(offset))
            .ToList();

        var rows = new List<ReceptionistLeaveReportLineDto>();

        foreach (var r in receptionists)
        {
            var rosteredDaysOfWeek = rosteredDaysByReceptionist.TryGetValue(r.Id, out var d)
                ? d : new HashSet<byte>();
            var shiftsPerWeek = shiftCountByReceptionist.TryGetValue(r.Id, out var sc) ? sc : 0;

            // Nobody rostered yet — nothing to compute leave against for this period.
            if (shiftsPerWeek == 0) continue;

            var present = presentByReceptionist.TryGetValue(r.Id, out var p) ? p : new HashSet<DateOnly>();

            var rosteredCount = 0;
            var presentCount = 0;
            foreach (var day in calendarDays)
            {
                if (!rosteredDaysOfWeek.Contains((byte)day.DayOfWeek)) continue;
                rosteredCount++;
                if (present.Contains(day)) presentCount++;
            }

            var taken = Math.Max(0, rosteredCount - presentCount);
            var entitlement = EntitlementForPeriod(periodType, shiftsPerWeek, start, end);

            existingDecisions.TryGetValue(r.Id, out var decision);

            rows.Add(new ReceptionistLeaveReportLineDto(
                r.Id, r.Name, r.Branch, shiftsPerWeek,
                entitlement, taken, taken > entitlement,
                decision?.DeductionPercent ?? 0m,
                decision?.Applied ?? false,
                decision?.Notes));
        }

        return Result<ReceptionistLeaveReportDto>.Success(new ReceptionistLeaveReportDto(
            periodType, start, end,
            rows.OrderBy(r => r.Branch).ThenBy(r => r.ReceptionistName).ToList(),
            DateTime.Now));
    }

    /// <summary>
    /// Records the Admin's deduction decision for one receptionist's period.
    /// Entitlement/taken are recomputed fresh rather than trusted from the
    /// request, the same way an amount is never trusted from the browser
    /// elsewhere in this service, so the stored row always reflects what was
    /// actually true at the moment the decision was made.
    /// </summary>
    public async Task<Result<bool>> SetLeaveDeductionAsync(
        ReceptionistLeaveDeductionRequest request, string? actor, CancellationToken ct = default)
    {
        var periodType = request.PeriodType is "Weekly" or "Yearly" ? request.PeriodType : "Monthly";
        var (start, _) = PeriodRange(periodType, request.PeriodStart);

        var report = await GetLeaveReportAsync(
            new ReceptionistLeaveReportQuery { PeriodType = periodType, Date = start }, ct);

        var line = report.Value?.Rows.FirstOrDefault(r => r.ReceptionistId == request.ReceptionistId);
        if (line is null)
        {
            return Result<bool>.Failure(
                "No active rota was found for this receptionist in this period.", ResultStatus.NotFound);
        }

        var existing = await _db.ReceptionistLeaveDeductions
            .FirstOrDefaultAsync(x => x.ReceptionistId == request.ReceptionistId
                && x.PeriodType == periodType && x.PeriodStart == start, ct);

        // Only tell the receptionist when something about the decision actually
        // changes — re-saving the same percentage every time the screen is
        // reopened must not spam them with an identical message.
        var wasApplied = existing?.Applied ?? false;
        var previousPercent = existing?.DeductionPercent ?? 0m;
        var changed = wasApplied != request.Applied || previousPercent != request.DeductionPercent;

        if (existing is null)
        {
            _db.ReceptionistLeaveDeductions.Add(new ReceptionistLeaveDeduction
            {
                ReceptionistId = request.ReceptionistId,
                PeriodType = periodType,
                PeriodStart = start,
                EntitlementDays = line.EntitlementDays,
                TakenDays = line.TakenDays,
                DeductionPercent = request.DeductionPercent,
                Applied = request.Applied,
                Notes = request.Notes,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });
        }
        else
        {
            existing.EntitlementDays = line.EntitlementDays;
            existing.TakenDays = line.TakenDays;
            existing.DeductionPercent = request.DeductionPercent;
            existing.Applied = request.Applied;
            existing.Notes = request.Notes;
            existing.ModifiedBy = actor;
            existing.ModifiedOn = DateTime.Now;
        }

        await _db.SaveChangesAsync(ct);

        if (changed && request.Applied)
        {
            await NotifyDeductionAsync(request.ReceptionistId, periodType, start, line, request.DeductionPercent, actor, ct);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Drops a chat message to the receptionist themselves so a salary deduction
    /// is never something they only discover on payday — the same "don't let it
    /// just vanish silently" reasoning behind <c>CallService.logCallOutcome</c> on
    /// the Angular side. Best-effort: a receptionist with no login yet, or no chat
    /// access, must never block the deduction itself from being recorded — the
    /// decision above has already been saved by the time this runs.
    /// </summary>
    private async Task NotifyDeductionAsync(
        int receptionistId, string periodType, DateOnly periodStart,
        ReceptionistLeaveReportLineDto line, decimal deductionPercent, string? actor, CancellationToken ct)
    {
        var receptionist = await _db.Receptionists.AsNoTracking()
            .Where(r => r.Id == receptionistId)
            .Select(r => r.LoginId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(receptionist) || string.IsNullOrWhiteSpace(actor)) return;

        var periodLabel = periodType switch
        {
            "Weekly" => $"the week of {periodStart:d MMM yyyy}",
            "Yearly" => $"{periodStart.Year}",
            _ => periodStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
        };

        var text = $"A {deductionPercent}% salary deduction has been applied for {periodLabel} " +
                   $"— {line.TakenDays} day(s) leave taken against a {line.EntitlementDays} day entitlement.";

        var conversation = await _chat.StartOrGetDirectConversationAsync(actor, receptionist, ct);
        if (!conversation.Succeeded) return;

        await _chat.SendMessageAsync(actor, conversation.Value, new SendMessageRequest { Body = text }, ct);
    }

    /* --------------------------------------------------------- extra-shift bonus */

    /// <summary>
    /// A transfer, not new money: a day this receptionist covered an absent
    /// colleague's shift adds that day's actual rostered hours at their own
    /// hourly rate; a day a colleague covered <b>their</b> shift instead
    /// deducts their own hours for that day, since they did not work it. Both
    /// sides use this receptionist's own hourly rate — what they would have
    /// earned or lost for those hours, on their own pay structure. Not counted
    /// on a day already part of this receptionist's own roster: that is their
    /// ordinary shift, not an extra one.
    /// </summary>
    /// <remarks>
    /// The Staff Rota only ever stores one week's pattern — a row per
    /// (day-of-week, shift) — so both the hourly rate and the day-by-day
    /// coverage below replay that weekly pattern across every calendar day of
    /// the relevant pay cycle, exactly as the day-counting version of this
    /// method already did, just summing hours (from <c>AttendanceShift</c>'s
    /// actual start/end times) instead of a flat one-shift-per-day count. A
    /// day can carry more than one shift (e.g. someone rostered Morning AND
    /// Evening on the same day-of-week), so hours are summed per day, not
    /// capped at one shift.
    /// </remarks>
    public async Task<Result<ExtraShiftBonusDto>> GetExtraShiftBonusAsync(
        int receptionistId, DateOnly forMonth, CancellationToken ct = default)
    {
        var me = await _db.Receptionists.AsNoTracking()
            .Where(r => r.Id == receptionistId)
            .Select(r => new { r.Branch })
            .FirstOrDefaultAsync(ct);

        if (me is null)
            return Result<ExtraShiftBonusDto>.Failure("Receptionist not found.", ResultStatus.NotFound);

        var branchBonuses = await ComputeBranchExtraShiftBonusesAsync(me.Branch, forMonth, ct);

        return branchBonuses.TryGetValue(receptionistId, out var dto)
            ? Result<ExtraShiftBonusDto>.Success(dto)
            : Result<ExtraShiftBonusDto>.Success(new ExtraShiftBonusDto(receptionistId, 0m, 0m, 0m, 0, 0, 0m, [], []));
    }

    /// <summary>
    /// Works out every active receptionist's extra-shift transfer for one
    /// branch and pay cycle, all at once. Doing it branch-wide instead of one
    /// receptionist at a time matters for correctness, not just efficiency: a
    /// single physical presence can only ever cover one absent colleague's
    /// shift on a given day, so when two colleagues are absent on the same
    /// day-of-week and only one person is on hand to cover, that one covering
    /// day must be attributed to exactly one absentee — never charged against
    /// both of them independently, which is what happened when each
    /// receptionist's transfer was computed in isolation. Matching absentees
    /// to coverers once, per day, for the whole branch together, is what keeps
    /// a covering day paired 1:1 on both sides of the transfer.
    /// </summary>
    /// <remarks>
    /// The Staff Rota only ever stores one week's pattern — a row per
    /// (day-of-week, shift) — so both the hourly rate and the day-by-day
    /// coverage below replay that weekly pattern across every calendar day of
    /// the relevant pay cycle, summing hours (from <c>AttendanceShift</c>'s
    /// actual start/end times), not a flat one-shift-per-day count. A day can
    /// carry more than one shift (e.g. someone rostered Morning AND Evening on
    /// the same day-of-week), so hours are summed per day, not capped at one
    /// shift. Absentees and coverers on the same day are paired by ascending
    /// receptionist id — deterministic, since attendance carries no shift
    /// identity to pair them by anything more specific.
    /// </remarks>
    private async Task<Dictionary<int, ExtraShiftBonusDto>> ComputeBranchExtraShiftBonusesAsync(
        string? branch, DateOnly forMonth, CancellationToken ct)
    {
        // Which shifts count as extra is a pay-period question — the institute's
        // configured cycle (26th-to-26th by default), not the calendar month —
        // so the coverage window uses PayCycleRange.
        var payCycleStartDay = await GetPayCycleStartDayAsync(ct);
        var (monthStart, monthEnd) = PayCycleRange(forMonth, payCycleStartDay);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var countEnd = monthEnd > today ? today : monthEnd;

        var people = await _db.Receptionists.AsNoTracking()
            .Where(r => r.IsActive && r.Branch == branch)
            .Select(r => new { r.Id, r.Name, r.MonthlyPay })
            .ToListAsync(ct);

        var ids = people.Select(p => p.Id).ToList();

        // --- 1. every active shift, with its actual clock window.
        var shifts = await _db.AttendanceShifts.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { s.Id, s.StartTime, s.EndTime })
            .ToListAsync(ct);

        var shiftById = shifts.ToDictionary(s => s.Id);

        // --- 2. everyone's weekly rota as (staff, day-of-week) -> the shifts
        // they are down for; one day-of-week can carry more than one shift.
        var roster = await _db.StaffShiftAssignments.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.IsActive && ids.Contains(a.StaffId))
            .Select(a => new { a.StaffId, a.DayOfWeek, a.ShiftId })
            .ToListAsync(ct);

        var rostered = roster
            .Where(a => shiftById.ContainsKey(a.ShiftId))
            .Select(a => (a.StaffId, a.DayOfWeek, a.ShiftId))
            .ToHashSet();

        decimal HoursFor(int staffId, byte dow) => shifts
            .Where(s => rostered.Contains((staffId, dow, s.Id)))
            .Sum(s => ShiftHours(s.StartTime, s.EndTime));

        // --- 3. everyone's own hourly rate: their MASTER standard monthly pay
        // (Receptionist.MonthlyPay, set on the fees master screen — never a
        // recorded payment, which can lag behind, be corrected after the fact,
        // or simply not exist yet for a new hire) divided by the hours the
        // weekly rota puts them down for across THIS pay cycle — the same
        // cycle whose pay is being adjusted. Kept unrounded: rounding the rate
        // first and multiplying after lost paise on every covered shift, so
        // only the final amounts are rounded.
        var cycleHours = new Dictionary<int, decimal>();
        var hourlyRate = new Dictionary<int, decimal>();

        foreach (var p in people)
        {
            var hrs = 0m;
            for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
                hrs += HoursFor(p.Id, (byte)d.DayOfWeek);

            cycleHours[p.Id] = hrs;
            hourlyRate[p.Id] = p.MonthlyPay <= 0m || hrs <= 0m ? 0m : p.MonthlyPay / hrs;
        }

        var coveredForOthers = people.ToDictionary(p => p.Id, _ => new List<ExtraShiftDayDto>());
        var coveredByOthers = people.ToDictionary(p => p.Id, _ => new List<ExtraShiftDayDto>());

        if (countEnd >= monthStart)
        {
            // --- 4. every In/Out scan at the branch, turned into time-on-site
            // sessions per person per day.
            var scans = await _db.StaffAttendance.AsNoTracking()
                .Where(a => a.StaffType == "Receptionist"
                    && a.Branch == branch && a.OnDate >= monthStart && a.OnDate <= countEnd
                    && ids.Contains(a.StaffId))
                .Select(a => new { a.StaffId, a.OnDate, a.Direction, a.ScannedOn })
                .ToListAsync(ct);

            var sessions = scans
                .GroupBy(a => (a.StaffId, a.OnDate))
                .ToDictionary(
                    g => g.Key,
                    g => BuildSessions(
                        g.OrderBy(a => a.ScannedOn).Select(a => (a.Direction, TimeOnly.FromDateTime(a.ScannedOn))),
                        shifts.Select(s => s.EndTime)));

            // Present for a shift = on site for at least half of it. Attendance
            // is a day's In/Out scans with no shift attached, so this is what
            // tells a person who stayed on for a colleague's evening apart from
            // one who only did their own morning, and a proper cover apart from
            // someone who dropped in for a few minutes.
            bool PresentFor(int staffId, DateOnly date, TimeOnly start, TimeOnly end)
            {
                if (!sessions.TryGetValue((staffId, date), out var mine)) return false;
                var overlap = mine.Sum(s => OverlapHours(s.From, s.To, start, end));
                return overlap * 2 >= ShiftHours(start, end);
            }

            for (var date = monthStart; date <= countEnd; date = date.AddDays(1))
            {
                var dow = (byte)date.DayOfWeek;

                // Each shift is settled on its own: who was down for it and
                // missed it, and who was there for it without being down for it
                // — including someone doing it on top of their own shift that day.
                foreach (var shift in shifts.OrderBy(s => s.StartTime))
                {
                    var hours = ShiftHours(shift.StartTime, shift.EndTime);

                    var absentees = people
                        .Where(p => rostered.Contains((p.Id, dow, shift.Id))
                            && !PresentFor(p.Id, date, shift.StartTime, shift.EndTime))
                        .OrderBy(p => p.Id)
                        .ToList();

                    if (absentees.Count == 0) continue;

                    var coverers = people
                        .Where(p => !rostered.Contains((p.Id, dow, shift.Id))
                            && PresentFor(p.Id, date, shift.StartTime, shift.EndTime))
                        .OrderBy(p => p.Id)
                        .ToList();

                    var pairCount = Math.Min(absentees.Count, coverers.Count);
                    for (var i = 0; i < pairCount; i++)
                    {
                        var absent = absentees[i];
                        var coverer = coverers[i];

                        coveredByOthers[absent.Id].Add(new ExtraShiftDayDto(
                            date, coverer.Name, branch, hours, Math.Round(hours * hourlyRate[absent.Id], 2)));

                        coveredForOthers[coverer.Id].Add(new ExtraShiftDayDto(
                            date, absent.Name, branch, hours, Math.Round(hours * hourlyRate[coverer.Id], 2)));
                    }
                }
            }
        }

        return people.ToDictionary(p => p.Id, p =>
        {
            var gains = coveredForOthers[p.Id];
            var losses = coveredByOthers[p.Id];

            // The sum of the lines shown on the payslip and in the chat notice,
            // so anyone adding them up by hand lands on exactly this figure.
            var netBonus = gains.Sum(d => d.Amount) - losses.Sum(d => d.Amount);

            return new ExtraShiftBonusDto(
                p.Id, Math.Round(hourlyRate[p.Id], 2), cycleHours[p.Id], p.MonthlyPay,
                gains.Count, losses.Count, netBonus, gains, losses);
        });
    }

    /// <summary>
    /// Pairs a day's scans into on-site sessions. An In with no Out after it is
    /// closed at the end of the shift it was scanned into (the first shift
    /// ending after it) — a forgotten Out scan should not stretch one shift
    /// into the rest of the day, nor erase it.
    /// </summary>
    private static List<(TimeOnly From, TimeOnly To)> BuildSessions(
        IEnumerable<(string Direction, TimeOnly At)> scans, IEnumerable<TimeOnly> shiftEnds)
    {
        var ends = shiftEnds.OrderBy(e => e).ToList();
        var result = new List<(TimeOnly From, TimeOnly To)>();
        TimeOnly? openFrom = null;

        TimeOnly CloseAt(TimeOnly from) => ends.FirstOrDefault(e => e > from, from);

        foreach (var (direction, at) in scans)
        {
            if (direction == "In")
            {
                if (openFrom is { } prev) result.Add((prev, CloseAt(prev)));
                openFrom = at;
            }
            else if (openFrom is { } from)
            {
                result.Add((from, at));
                openFrom = null;
            }
        }

        if (openFrom is { } last) result.Add((last, CloseAt(last)));
        return result;
    }

    private static decimal OverlapHours(TimeOnly aFrom, TimeOnly aTo, TimeOnly bFrom, TimeOnly bTo)
    {
        var from = aFrom > bFrom ? aFrom : bFrom;
        var to = aTo < bTo ? aTo : bTo;
        return to > from ? (decimal)(to - from).TotalHours : 0m;
    }

    private static decimal ShiftHours(TimeOnly start, TimeOnly end)
    {
        var span = end - start;
        if (span < TimeSpan.Zero) span += TimeSpan.FromDays(1);
        return (decimal)span.TotalHours;
    }

    /// <summary>
    /// Tells every active receptionist at the branch — not just the two
    /// involved — why one receptionist's payment moved for an extra-shift
    /// adjustment, so the transfer is never something colleagues only guess
    /// at from a number on someone else's payslip. Fires for every
    /// receptionist's payment, not just a chosen few. Best-effort, same as
    /// <see cref="NotifyDeductionAsync"/>: a chat failure must never undo the
    /// payment that already saved.
    /// </summary>
    private async Task NotifyExtraShiftBonusAsync(
        string receptionistName, string? branch, DateOnly payMonth,
        ExtraShiftBonusDto bonus, string? actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) return;
        if (bonus.CoveredForOthersCount == 0 && bonus.CoveredByOthersCount == 0) return;

        var lines = new List<string>();

        foreach (var day in bonus.CoveredForOthers)
            lines.Add($"{day.Date:d MMM}: {receptionistName} covered {day.OtherReceptionistName}'s " +
                      $"{day.Hours:0.##}h shift and earned ₹{day.Amount:0.##} extra.");

        foreach (var day in bonus.CoveredByOthers)
            lines.Add($"{day.Date:d MMM}: {day.OtherReceptionistName} covered {receptionistName}'s " +
                      $"{day.Hours:0.##}h shift, so ₹{day.Amount:0.##} was deducted from {receptionistName}'s pay.");

        var text = $"Extra-shift adjustment for {receptionistName} — {payMonth:MMMM yyyy}:\n" +
                   string.Join("\n", lines);

        var recipients = await _db.Receptionists.AsNoTracking()
            .Where(r => r.IsActive && r.Branch == branch && r.LoginId != null)
            .Select(r => r.LoginId!)
            .ToListAsync(ct);

        foreach (var loginId in recipients)
        {
            var conversation = await _chat.StartOrGetDirectConversationAsync(actor, loginId, ct);
            if (!conversation.Succeeded) continue;

            await _chat.SendMessageAsync(actor, conversation.Value, new SendMessageRequest { Body = text }, ct);
        }
    }
}
