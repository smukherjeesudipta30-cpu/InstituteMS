using InstituteMS.Application.Common;
using InstituteMS.Application.Expenses;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The institute expense register. Replaces <c>ExpenseController</c> plus the
/// <c>Expense</c> half of <c>Models/Expense.cs</c> (MIGRATION_PLAN §4.6, §5.10).
/// </summary>
/// <remarks>
/// <para>
/// Every legacy statement here was string-concatenated (defect D1), and the insert
/// used <c>Insert into Expense values (...)</c> with no column list - so adding a
/// column to the table would have broken saving silently.
/// </para>
/// <para>
/// The legacy page also loaded the entire table on every render and paged it in the
/// browser, which is why its running total only ever covered the rows on screen.
/// Paging and totalling are both server-side here.
/// </para>
/// </remarks>
public class ExpenseService : IExpenseService
{
    private readonly AppDbContext _db;

    public ExpenseService(AppDbContext db) => _db = db;

    public async Task<Result<ExpenseListDto>> GetAsync(
        ExpenseQuery query, CancellationToken ct = default)
    {
        if (query.From.HasValue && query.To.HasValue && query.To < query.From)
            return Result<ExpenseListDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var q = _db.Expenses.AsNoTracking();

        if (query.From.HasValue)
            q = q.Where(e => e.ExpenseDate != null && e.ExpenseDate >= query.From);

        if (query.To.HasValue)
            q = q.Where(e => e.ExpenseDate != null && e.ExpenseDate <= query.To);

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
            // Ext is null for expenses the legacy application recorded, so the
            // branch is read defensively and reported as unattributed.
            .Select(e => new ExpenseListItemDto(
                e.Id, e.Details, e.ExpenseDate, e.Amount ?? 0m,
                e.Ext != null ? e.Ext.Branch : null))
            .ToListAsync(ct);

        var rows = PagedResult<ExpenseListItemDto>.Create(items, query.Page, query.PageSize, entryCount);

        return Result<ExpenseListDto>.Success(new ExpenseListDto(
            rows, new ExpenseSummaryDto(entryCount, total, query.From, query.To)));
    }

    /// <summary>
    /// Matches the submitted branch against the branch register.
    /// </summary>
    /// <remarks>
    /// The column is free text and the screen offers a dropdown, but the API takes
    /// whatever it is sent. Since the branch now decides which column of the branch
    /// report an expense lands in, a typo would create a phantom branch that quietly
    /// splits the figures. Matched case-insensitively and stored in the register's
    /// own spelling, so "rajpur" and "Rajpur" cannot become two branches.
    /// </remarks>
    private async Task<Result<string>> ResolveBranchAsync(string? branch, CancellationToken ct)
    {
        var name = branch?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Result<string>.Failure("Choose the branch this expense belongs to.");

        var known = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Name != null && b.Name.ToLower() == name.ToLower())
            .Select(b => b.Name!)
            .FirstOrDefaultAsync(ct);

        return known is null
            ? Result<string>.Failure($"'{name}' is not a branch on record.")
            : Result<string>.Success(known);
    }

    public async Task<Result<int>> CreateAsync(
        ExpenseSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<int>.Failure(branch.Error!, branch.Status);

        var expense = new Expense
        {
            Details = request.Details.Trim(),
            // The legacy form defaulted the picker to today; an omitted date means
            // the same thing rather than a null row.
            ExpenseDate = request.ExpenseDate ?? DateOnly.FromDateTime(DateTime.Now),
            Amount = request.Amount,

            // Branch and audit cannot live on dbo.Expense - the legacy
            // application bare-inserts into it - so they go to the side row,
            // which EF writes in the same transaction.
            Ext = new ExpenseExt
            {
                Branch = branch.Value,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            }
        };

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(expense.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int expenseId, ExpenseSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var expense = await _db.Expenses
            .Include(e => e.Ext)
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

        if (expense is null)
            return Result<bool>.Failure("That expense no longer exists.", ResultStatus.NotFound);

        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<bool>.Failure(branch.Error!, branch.Status);

        expense.Details = request.Details.Trim();
        expense.Amount = request.Amount;

        // An expense the legacy application recorded has no side row yet; the
        // first edit here creates one.
        expense.Ext ??= new ExpenseExt { ExpenseId = expense.Id, CreatedOn = DateTime.Now };
        expense.Ext.Branch = branch.Value;

        // On update an omitted date keeps the stored one. The legacy update wrote
        // whatever the form held, so re-saving a row after only fixing a typo in the
        // details could move the expense to a different day.
        if (request.ExpenseDate.HasValue)
            expense.ExpenseDate = request.ExpenseDate;

        expense.Ext.ModifiedBy = actor;
        expense.Ext.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int expenseId, CancellationToken ct = default)
    {
        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, ct);

        if (expense is null)
            return Result<bool>.Failure("That expense no longer exists.", ResultStatus.NotFound);

        // Expenses are not referenced by anything else, so this stays a hard delete -
        // the same as the legacy behaviour. The confirmation now lives in the UI,
        // which the legacy grid did not have.
        _db.Expenses.Remove(expense);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
