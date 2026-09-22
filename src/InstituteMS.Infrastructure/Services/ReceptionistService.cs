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
                r.Payments.Count,
                r.Payments.Sum(p => (decimal?)(p.Amount + p.ExtraShiftBonus)) ?? 0m,
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
            existing.Amount = request.Amount;
            existing.ExtraShiftBonus = extraShiftBonus;
            existing.PaidOn = request.PaidOn;
            existing.Notes = request.Notes?.Trim();
            // The branch is refreshed from the person, so correcting a payment after
            // they move branch puts it where they are now.
            existing.Branch = person.Branch;

            await _db.SaveChangesAsync(ct);

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

        return Result<int>.Success(payment.Id);
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
                p.CreatedBy, p.CreatedOn))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ReceptionistPaymentDto>>.Success(rows);
    }

    public async Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.ReceptionistPayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null)
            return Result<bool>.Failure("That payment no longer exists.", ResultStatus.NotFound);

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
                Amount = p.Amount + p.ExtraShiftBonus
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
    /// colleague's shift adds one shift at their own rate; a day a colleague
    /// covered <b>their</b> shift instead deducts one, since they did not work
    /// it. Both sides use this receptionist's own per-shift rate — what they
    /// would have earned or lost for that one day, on their own pay structure.
    /// Not counted on a day already part of this receptionist's own roster:
    /// that is their ordinary shift, not an extra one.
    /// </summary>
    public async Task<Result<ExtraShiftBonusDto>> GetExtraShiftBonusAsync(
        int receptionistId, DateOnly forMonth, CancellationToken ct = default)
    {
        var me = await _db.Receptionists.AsNoTracking()
            .Where(r => r.Id == receptionistId)
            .Select(r => new { r.Branch })
            .FirstOrDefaultAsync(ct);

        if (me is null)
            return Result<ExtraShiftBonusDto>.Failure("Receptionist not found.", ResultStatus.NotFound);

        // Two different "months" deliberately kept apart. A ReceptionistPayment
        // is always keyed to a calendar month-start (RecordPaymentAsync normalises
        // it), so the payment lookup below stays calendar-month. Which SHIFTS
        // count as extra, though, is a pay-period question — the institute's
        // configured cycle (26th-to-26th by default), not the calendar month —
        // so the coverage window uses PayCycleRange instead.
        var paymentLookupMonth = MonthStart(forMonth);
        var payCycleStartDay = await GetPayCycleStartDayAsync(ct);
        var (monthStart, monthEnd) = PayCycleRange(forMonth, payCycleStartDay);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var countEnd = monthEnd > today ? today : monthEnd;

        // --- 1. the per-shift rate: last recorded BASE payment (never the bonus —
        // see ReceptionistPayment.ExtraShiftBonus), divided by however many shifts
        // this receptionist was rostered for at the time. Using the base alone
        // keeps the rate stable: a bonus paid one cycle must never inflate the
        // rate the next cycle's bonus is computed from.
        var lastPayment = await _db.ReceptionistPayments.AsNoTracking()
            .Where(p => p.ReceptionistId == receptionistId && p.PayMonth < paymentLookupMonth)
            .OrderByDescending(p => p.PayMonth)
            .FirstOrDefaultAsync(ct);

        var ownShiftsNow = await _db.StaffShiftAssignments.AsNoTracking()
            .CountAsync(a => a.StaffType == "Receptionist" && a.StaffId == receptionistId && a.IsActive, ct);

        var perShiftRate = lastPayment is null || ownShiftsNow == 0
            ? 0m
            : Math.Round(lastPayment.Amount / ownShiftsNow, 2);

        if (perShiftRate == 0m || countEnd < monthStart)
        {
            return Result<ExtraShiftBonusDto>.Success(new ExtraShiftBonusDto(
                receptionistId, perShiftRate, ownShiftsNow, lastPayment?.Amount ?? 0m, 0, 0, 0m, [], []));
        }

        // --- 2. every OTHER receptionist at the same branch, and their roster.
        var colleagues = await _db.Receptionists.AsNoTracking()
            .Where(r => r.IsActive && r.Id != receptionistId && r.Branch == me.Branch)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);

        var colleagueIds = colleagues.Select(c => c.Id).ToList();

        var colleagueRoster = await _db.StaffShiftAssignments.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.IsActive && colleagueIds.Contains(a.StaffId))
            .Select(a => new { a.StaffId, a.DayOfWeek })
            .ToListAsync(ct);

        var rosterByColleague = colleagueRoster
            .GroupBy(a => a.StaffId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.DayOfWeek).ToHashSet());

        var myOwnDaysOfWeek = (await _db.StaffShiftAssignments.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.StaffId == receptionistId && a.IsActive)
            .Select(a => a.DayOfWeek)
            .ToListAsync(ct)).ToHashSet();

        // --- 3. who scanned in ("In") on which day, everyone involved, at the branch.
        var everyoneIds = colleagueIds.Append(receptionistId).ToList();

        var presentByDay = (await _db.StaffAttendance.AsNoTracking()
            .Where(a => a.StaffType == "Receptionist" && a.Direction == "In"
                && a.Branch == me.Branch && a.OnDate >= monthStart && a.OnDate <= countEnd
                && everyoneIds.Contains(a.StaffId))
            .Select(a => new { a.StaffId, a.OnDate })
            .Distinct()
            .ToListAsync(ct))
            .GroupBy(a => a.OnDate)
            .ToDictionary(g => g.Key, g => g.Select(a => a.StaffId).ToHashSet());

        var coveredForOthers = new List<ExtraShiftDayDto>();
        var coveredByOthers = new List<ExtraShiftDayDto>();

        for (var date = monthStart; date <= countEnd; date = date.AddDays(1))
        {
            var dow = (byte)date.DayOfWeek;
            var presentToday = presentByDay.TryGetValue(date, out var p) ? p : new HashSet<int>();
            var iAmPresent = presentToday.Contains(receptionistId);

            // Gain: not this receptionist's own rostered day, they scanned in
            // anyway, and a colleague actually rostered for it did not.
            if (!myOwnDaysOfWeek.Contains(dow) && iAmPresent)
            {
                var absentColleague = colleagues.FirstOrDefault(c =>
                    rosterByColleague.TryGetValue(c.Id, out var rosterDays)
                    && rosterDays.Contains(dow)
                    && !presentToday.Contains(c.Id));

                if (absentColleague is not null)
                    coveredForOthers.Add(new ExtraShiftDayDto(date, absentColleague.Name, me.Branch));
            }

            // Loss: this receptionist's own rostered day, they did NOT scan in,
            // and some other colleague (not rostered that day) stood in instead.
            if (myOwnDaysOfWeek.Contains(dow) && !iAmPresent)
            {
                var coveringColleague = colleagues.FirstOrDefault(c =>
                    presentToday.Contains(c.Id)
                    && !(rosterByColleague.TryGetValue(c.Id, out var rosterDays) && rosterDays.Contains(dow)));

                if (coveringColleague is not null)
                    coveredByOthers.Add(new ExtraShiftDayDto(date, coveringColleague.Name, me.Branch));
            }
        }

        var netBonus = Math.Round((coveredForOthers.Count - coveredByOthers.Count) * perShiftRate, 2);

        return Result<ExtraShiftBonusDto>.Success(new ExtraShiftBonusDto(
            receptionistId, perShiftRate, ownShiftsNow, lastPayment?.Amount ?? 0m,
            coveredForOthers.Count, coveredByOthers.Count, netBonus,
            coveredForOthers, coveredByOthers));
    }
}
