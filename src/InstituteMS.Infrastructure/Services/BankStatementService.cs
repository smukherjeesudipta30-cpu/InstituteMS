using System.Text.Json;
using InstituteMS.Application.BankStatements;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Monthly stock and book-debt statements for the bank, entered by Admin. A
/// month that has never been saved opens with the latest earlier month's
/// figures, so only what changed needs editing.
/// </summary>
public class BankStatementService : IBankStatementService
{
    private const int MaxDataLength = 1_000_000;

    private readonly AppDbContext _db;

    public BankStatementService(AppDbContext db) => _db = db;

    public async Task<BankStatementDto> GetAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var saved = await _db.BankStatements.AsNoTracking().FirstOrDefaultAsync(s => s.MonthStart == start, ct);
        if (saved is not null)
            return new BankStatementDto(Key(start), true, null, saved.DataJson, saved.ModifiedBy, saved.ModifiedOn);

        var previous = await _db.BankStatements.AsNoTracking()
            .Where(s => s.MonthStart < start)
            .OrderByDescending(s => s.MonthStart)
            .FirstOrDefaultAsync(ct);

        return previous is null
            ? new BankStatementDto(Key(start), false, null, "{}", null, null)
            : new BankStatementDto(Key(start), false, Key(previous.MonthStart), previous.DataJson, null, null);
    }

    public async Task<Result<BankStatementDto>> SaveAsync(int year, int month, string data, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(data) || data.Length > MaxDataLength)
            return Result<BankStatementDto>.Failure("The statement is empty or too large.");
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Result<BankStatementDto>.Failure("The statement is not in the expected format.");
        }
        catch (JsonException)
        {
            return Result<BankStatementDto>.Failure("The statement is not in the expected format.");
        }

        var start = new DateOnly(year, month, 1);
        var row = await _db.BankStatements.FirstOrDefaultAsync(s => s.MonthStart == start, ct);
        if (row is null)
        {
            row = new BankStatement { MonthStart = start };
            _db.BankStatements.Add(row);
        }
        row.DataJson = data;
        row.ModifiedBy = actor;
        row.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<BankStatementDto>.Success(new BankStatementDto(Key(start), true, null, data, actor, row.ModifiedOn));
    }

    public async Task<IReadOnlyList<BookDebtRowDto>> GetBookDebtsAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive == true && s.RegistrationId != null
                        && s.DateOfJoining != null && s.DateOfJoining <= end)
            .Select(s => new
            {
                RegistrationId = s.RegistrationId!,
                Branch = s.Branch ?? "",
                JoinedOn = s.DateOfJoining!.Value,
                MonthlyFee = s.MonthlyFees ?? 0m
            })
            .ToListAsync(ct);

        var regIds = students.Select(s => s.RegistrationId).ToList();
        var payments = await _db.MonthlyFees.AsNoTracking()
            .Where(f => f.StudentRegistrationId != null && f.FeeDate != null && f.FeeDate <= end
                        && regIds.Contains(f.StudentRegistrationId))
            .Select(f => new { RegistrationId = f.StudentRegistrationId!, FeeDate = f.FeeDate!.Value, f.Id, f.NewDue, f.Installment })
            .ToListAsync(ct);
        var byStudent = payments.ToLookup(p => p.RegistrationId, StringComparer.OrdinalIgnoreCase);

        // What one student owes on a date: the balance left at their last payment
        // on or before it, plus every 32-day fee month gone by since without one —
        // the same rule as the dues and pending-payments reports. No late fine: a
        // fine is not billed until it is charged at the counter.
        (decimal Owed, int AgeDays) OwedOn(DateOnly asOf, string regId, DateOnly joinedOn, decimal monthlyFee)
        {
            if (joinedOn > asOf) return (0m, 0);
            var last = byStudent[regId].Where(p => p.FeeDate <= asOf)
                .OrderByDescending(p => p.FeeDate).ThenByDescending(p => p.Id).FirstOrDefault();
            var since = last?.FeeDate ?? joinedOn;
            var carried = Math.Max(0m, last?.NewDue ?? 0m);
            var days = asOf.DayNumber - since.DayNumber;
            return (carried + days / LateFineCalculator.GraceDays * monthlyFee, days);
        }

        var openingDate = start.AddDays(-1);
        var rows = new List<BookDebtRowDto>();
        foreach (var branch in students.GroupBy(s => s.Branch, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            decimal opening = 0, received = 0, net = 0, a90 = 0, a120 = 0, a180 = 0, above = 0;
            var owing = 0;
            foreach (var s in branch)
            {
                opening += OwedOn(openingDate, s.RegistrationId, s.JoinedOn, s.MonthlyFee).Owed;
                received += byStudent[s.RegistrationId].Where(p => p.FeeDate >= start).Sum(p => p.Installment ?? 0m);

                var (owed, age) = OwedOn(end, s.RegistrationId, s.JoinedOn, s.MonthlyFee);
                if (owed <= 0) continue;
                owing++;
                net += owed;
                if (age <= 90) a90 += owed;
                else if (age <= 120) a120 += owed;
                else if (age <= 180) a180 += owed;
                else above += owed;
            }

            if (opening == 0 && received == 0 && net == 0) continue;
            var name = string.IsNullOrWhiteSpace(branch.Key) ? "Students (no branch)" : $"Students - {branch.Key} branch";
            rows.Add(new BookDebtRowDto(name, owing, opening, net - opening + received, received, 0m, net, a90, a120, a180, above));
        }

        return rows;
    }

    private static string Key(DateOnly d) => $"{d.Year:D4}-{d.Month:D2}";
}
