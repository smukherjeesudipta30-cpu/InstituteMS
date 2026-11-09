using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Branch master. The legacy <c>Views/Branch/Index.cshtml</c> was an empty stub, but
/// the table is real and its code is now part of every registration id issued for
/// that branch (Rajpur becomes <c>AVI/Raj/3463/26</c>), so it needs a proper screen.
/// </summary>
public class BranchService : IBranchService
{
    private readonly AppDbContext _db;
    private readonly ILogger<BranchService> _logger;

    public BranchService(AppDbContext db, ILogger<BranchService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BranchListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var branches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id, b.Name, b.Code, b.IsActive,
                b.Latitude, b.Longitude, b.GeofenceMetres, b.UpiId, b.UpiPayeeName
            })
            .ToListAsync(ct);

        if (branches.Count == 0) return [];

        // Students carry the branch NAME (legacy shape), so count on that.
        var names = branches.Select(b => b.Name).Where(n => n != null).ToList();

        var studentCounts = await _db.Students
            .AsNoTracking()
            .Where(s => s.Branch != null && names.Contains(s.Branch))
            .GroupBy(s => s.Branch!)
            .Select(g => new { Branch = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Registration ids already issued under each code. Once any exist, changing
        // the code would leave those ids referring to a code that no longer exists.
        var registrationIds = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId != null && s.RegistrationId.StartsWith("AVI/"))
            .Select(s => s.RegistrationId!)
            .ToListAsync(ct);

        return branches.Select(b =>
        {
            var issued = string.IsNullOrWhiteSpace(b.Code)
                ? 0
                : registrationIds.Count(r =>
                    r.StartsWith($"AVI/{b.Code}/", StringComparison.OrdinalIgnoreCase));

            return new BranchListItemDto(
                b.Id, b.Name, b.Code, b.IsActive,
                studentCounts.FirstOrDefault(s =>
                    string.Equals(s.Branch, b.Name, StringComparison.OrdinalIgnoreCase))?.Count ?? 0,
                issued,
                b.Latitude, b.Longitude, b.GeofenceMetres, b.UpiId, b.UpiPayeeName);
        }).ToList();
    }

    public async Task<Result<int>> CreateAsync(
        BranchSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var name = request.Name!.Trim();
        var code = request.Code!.Trim();

        if (await _db.Branches.AnyAsync(b => b.Name == name, ct))
            return Result<int>.Failure($"A branch called '{name}' already exists.", ResultStatus.Conflict);

        if (await _db.Branches.AnyAsync(b => b.Code == code, ct))
            return Result<int>.Failure($"Branch code '{code}' is already in use.", ResultStatus.Conflict);

        var branch = new Branch
        {
            Name = name,
            Code = code,

            // Written to the legacy columns too, so the row is complete for both
            // applications and Is_active (NOT NULL) is satisfied.
            LegacyName = name,
            LegacyCode = code,
            LegacyIsActive = request.IsActive,
            IsActive = request.IsActive,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            GeofenceMetres = request.GeofenceMetres,
            UpiId = Clean(request.UpiId),
            UpiPayeeName = Clean(request.UpiPayeeName),
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Branch {BranchId} ({Name}/{Code}) created by {Actor}",
            branch.Id, name, code, actor);

        return Result<int>.Success(branch.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, BranchSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null)
            return Result<bool>.Failure("Branch not found.", ResultStatus.NotFound);

        var name = request.Name!.Trim();
        var code = request.Code!.Trim();

        if (await _db.Branches.AnyAsync(b => b.Name == name && b.Id != id, ct))
            return Result<bool>.Failure($"A branch called '{name}' already exists.", ResultStatus.Conflict);

        if (await _db.Branches.AnyAsync(b => b.Code == code && b.Id != id, ct))
            return Result<bool>.Failure($"Branch code '{code}' is already in use.", ResultStatus.Conflict);

        // The code is baked into every registration id issued under it. Changing it
        // afterwards would leave those students carrying a code that no longer maps
        // to any branch, so it is frozen once used.
        if (!string.Equals(branch.Code, code, StringComparison.Ordinal))
        {
            var issued = await CountIssuedAsync(branch.Code, ct);

            if (issued > 0)
            {
                return Result<bool>.Failure(
                    $"The code cannot be changed: {issued} registration id(s) were already issued as " +
                    $"AVI/{branch.Code}/... Rename the branch if you need to, but leave the code alone.",
                    ResultStatus.Conflict);
            }
        }

        // Renaming is allowed, but Student.Branch stores the name, so existing rows
        // are carried over to keep them pointing at this branch.
        var oldName = branch.Name;

        branch.Name = name;
        branch.Code = code;
        branch.IsActive = request.IsActive;
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;
        branch.GeofenceMetres = request.GeofenceMetres;
        branch.UpiId = Clean(request.UpiId);
        branch.UpiPayeeName = Clean(request.UpiPayeeName);

        branch.LegacyName = name;
        branch.LegacyCode = code;
        branch.LegacyIsActive = request.IsActive;

        if (!string.Equals(oldName, name, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(oldName))
        {
            var affected = await _db.Students
                .Where(s => s.Branch == oldName)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Branch, name), ct);

            if (affected > 0)
                _logger.LogInformation("Branch renamed {Old} to {New}; {Count} student(s) updated",
                    oldName, name, affected);
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetStatusAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch is null)
            return Result<bool>.Failure("Branch not found.", ResultStatus.NotFound);

        branch.IsActive = isActive;
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    private async Task<int> CountIssuedAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;

        var prefix = $"AVI/{code}/";

        return await _db.Students.CountAsync(
            s => s.RegistrationId != null && s.RegistrationId.StartsWith(prefix), ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
