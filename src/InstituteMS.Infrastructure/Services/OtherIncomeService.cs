using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The non-fee income register — anything earned that is not a student fee or
/// admission fee.
/// </summary>
/// <remarks>
/// <c>dbo.OIncome</c> was read-only until now: 102 migrated rows with no screen,
/// no endpoint and no branch. It feeds the "Other income" line of the income
/// report, so without a way to add to it that line could only ever shrink into
/// history.
/// </remarks>
public class OtherIncomeService : IOtherIncomeService
{
    private readonly AppDbContext _db;

    public OtherIncomeService(AppDbContext db) => _db = db;

    public async Task<Result<OtherIncomeListDto>> GetAsync(
        OtherIncomeQuery query, CancellationToken ct = default)
    {
        if (query.From.HasValue && query.To.HasValue && query.To < query.From)
            return Result<OtherIncomeListDto>.Failure("The 'to' date cannot be before the 'from' date.");

        var q = _db.OtherIncomes.AsNoTracking();

        if (query.From.HasValue) q = q.Where(i => i.IncomeDate >= query.From);
        if (query.To.HasValue) q = q.Where(i => i.IncomeDate <= query.To);

        if (!string.IsNullOrWhiteSpace(query.Branch))
        {
            var branch = query.Branch.Trim();
            q = q.Where(i => i.Ext != null && i.Ext.Branch == branch);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(i => i.Purpose != null && EF.Functions.Like(i.Purpose, $"%{term}%"));
        }

        // Aggregated in SQL over the whole filter, not over the page the grid
        // happens to have drawn.
        var entryCount = await q.CountAsync(ct);
        var total = await q.SumAsync(i => (decimal?)i.Amount ?? 0m, ct);

        var items = await q
            .OrderByDescending(i => i.IncomeDate)
            .ThenByDescending(i => i.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new OtherIncomeListItemDto(
                i.Id, i.Purpose, i.IncomeDate, i.Amount ?? 0m,
                i.Ext != null ? i.Ext.Branch : null))
            .ToListAsync(ct);

        var rows = PagedResult<OtherIncomeListItemDto>.Create(
            items, query.Page, query.PageSize, entryCount);

        return Result<OtherIncomeListDto>.Success(new OtherIncomeListDto(
            rows, new OtherIncomeSummaryDto(entryCount, total, query.From, query.To)));
    }

    /// <summary>
    /// Checks the branch against the register. The column is free text, so a typo
    /// would create a phantom branch that quietly splits the income report.
    /// </summary>
    private async Task<Result<string>> ResolveBranchAsync(string? branch, CancellationToken ct)
    {
        var name = branch?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Result<string>.Failure("Choose the branch that earned this.");

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
        OtherIncomeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<int>.Failure(branch.Error!, branch.Status);

        var income = new OtherIncome
        {
            Purpose = request.Purpose.Trim(),
            IncomeDate = request.IncomeDate ?? DateOnly.FromDateTime(DateTime.Now),
            Amount = request.Amount,

            // dbo.OIncome is bare-inserted by the legacy application and cannot
            // gain columns, so branch and audit go to the side row.
            Ext = new OtherIncomeExt
            {
                Branch = branch.Value,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            }
        };

        _db.OtherIncomes.Add(income);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(income.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int incomeId, OtherIncomeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var income = await _db.OtherIncomes
            .Include(i => i.Ext)
            .FirstOrDefaultAsync(i => i.Id == incomeId, ct);

        if (income is null)
            return Result<bool>.Failure("That income entry no longer exists.", ResultStatus.NotFound);

        var branch = await ResolveBranchAsync(request.Branch, ct);

        if (!branch.Succeeded)
            return Result<bool>.Failure(branch.Error!, branch.Status);

        income.Purpose = request.Purpose.Trim();
        income.Amount = request.Amount;

        // Entries the legacy application recorded have no side row yet.
        income.Ext ??= new OtherIncomeExt { IncomeId = income.Id, CreatedOn = DateTime.Now };
        income.Ext.Branch = branch.Value;

        // An omitted date keeps the stored one, so re-saving after fixing a typo in
        // the purpose cannot move the entry to a different day.
        if (request.IncomeDate.HasValue)
            income.IncomeDate = request.IncomeDate;

        income.Ext.ModifiedBy = actor;
        income.Ext.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int incomeId, CancellationToken ct = default)
    {
        var income = await _db.OtherIncomes
            .Include(i => i.Ext)
            .FirstOrDefaultAsync(i => i.Id == incomeId, ct);

        if (income is null)
            return Result<bool>.Failure("That income entry no longer exists.", ResultStatus.NotFound);

        _db.OtherIncomes.Remove(income);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
