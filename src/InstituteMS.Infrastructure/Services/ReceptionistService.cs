using System.Globalization;
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

    public ReceptionistService(AppDbContext db) => _db = db;

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
                r.Payments.Sum(p => (decimal?)p.Amount) ?? 0m,
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

        // One payment per person per month. A second for the same month is a
        // correction to the first, so it updates rather than adding a second salary.
        var existing = await _db.ReceptionistPayments
            .FirstOrDefaultAsync(p => p.ReceptionistId == request.ReceptionistId && p.PayMonth == month, ct);

        if (existing is not null)
        {
            existing.Amount = request.Amount;
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
                p.PayMonth, p.PaidOn, p.Amount, p.Branch, p.Notes,
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
                p.Amount
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
}
