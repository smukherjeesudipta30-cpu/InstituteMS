using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The PreSchool store: tuition books, bags and the like, class by class. Stock
/// is received less given, worked out from the movements, so it can never
/// drift from the record of what happened. Reads and writes PreSchool* tables.
/// </summary>
public class PreSchoolStoreService : IPreSchoolStoreService
{
    private const string In = "In";
    private const string Out = "Out";

    private readonly AppDbContext _db;

    public PreSchoolStoreService(AppDbContext db) => _db = db;

    public async Task<PreSchoolStoreDto> GetAsync(CancellationToken ct = default)
    {
        var items = await LoadItemsAsync(null, ct);

        var recent = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Include(m => m.Item).Include(m => m.Student)
            .OrderByDescending(m => m.CreatedOn).Take(60)
            .ToListAsync(ct);
        var prefix = await PrefixAsync(ct);

        return new PreSchoolStoreDto(items, recent.Select(m => new PreSchoolStoreMovementDto(
            m.Id, m.ItemId, m.Item?.Name ?? "", m.Kind, m.Quantity, m.StudentId, m.Student?.ChildName,
            m.Student is null ? null : $"{prefix}-{m.Student.AdmissionNo:0000}", m.OnDate, m.Note, m.CreatedBy, m.UnitCost, m.Supplier)).ToList(),
            await AccountAsync(ct));
    }

    public async Task<Result<PreSchoolStoreItemDto>> SaveItemAsync(int? id, PreSchoolStoreItemSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.ClassId is > 0 && !await _db.PreSchoolClasses.AnyAsync(c => c.Id == request.ClassId, ct))
            return Result<PreSchoolStoreItemDto>.Failure("That class no longer exists.", ResultStatus.NotFound);

        var name = request.Name.Trim();
        var classId = request.ClassId is > 0 ? request.ClassId : null;
        if (await _db.PreSchoolStoreItems.AnyAsync(i => i.Id != id && i.ClassId == classId && i.Name == name, ct))
            return Result<PreSchoolStoreItemDto>.Failure("That class already has an item with this name.", ResultStatus.Conflict);

        PreSchoolStoreItem? item;
        if (id is null)
        {
            item = new PreSchoolStoreItem { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.PreSchoolStoreItems.Add(item);
        }
        else
        {
            item = await _db.PreSchoolStoreItems.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) return Result<PreSchoolStoreItemDto>.Failure("That item no longer exists.", ResultStatus.NotFound);
            item.ModifiedBy = actor;
            item.ModifiedOn = DateTime.Now;
        }

        item.Name = name;
        item.ClassId = classId;
        item.SortOrder = request.SortOrder;
        item.IsActive = request.IsActive;
        item.UnitCost = request.UnitCost;
        item.ChargeHead = string.IsNullOrWhiteSpace(request.ChargeHead) ? null : request.ChargeHead;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolStoreItemDto>.Success((await LoadItemsAsync(item.Id, ct)).Single());
    }

    public async Task<Result<PreSchoolStoreItemDto>> StockInAsync(int itemId, PreSchoolStockInRequest request, string? actor, CancellationToken ct = default)
    {
        var stocked = await _db.PreSchoolStoreItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (stocked is null)
            return Result<PreSchoolStoreItemDto>.Failure("That item no longer exists.", ResultStatus.NotFound);

        _db.PreSchoolStoreMovements.Add(new PreSchoolStoreMovement
        {
            ItemId = itemId, Kind = In, Quantity = request.Quantity,
            UnitCost = request.UnitCost ?? stocked.UnitCost,
            Supplier = string.IsNullOrWhiteSpace(request.Supplier) ? DefaultSupplier : request.Supplier.Trim(),
            OnDate = request.OnDate ?? DateOnly.FromDateTime(DateTime.Today),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedBy = actor, CreatedOn = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolStoreItemDto>.Success((await LoadItemsAsync(itemId, ct)).Single());
    }

    public async Task<Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>> ForStudentAsync(int studentId, CancellationToken ct = default)
    {
        var s = await _db.PreSchoolStudents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure("That child is not on the list.", ResultStatus.NotFound);

        var items = await LoadItemsAsync(null, ct);
        var given = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => m.StudentId == studentId && m.Kind == Out)
            .ToListAsync(ct);

        // The class's items that are in use, plus anything already given (even if since switched off).
        var lines = items
            .Where(i => (i.IsActive && (i.ClassId == null || i.ClassId == s.ClassId)) || given.Any(g => g.ItemId == i.Id))
            .Select(i =>
            {
                var g = given.Where(m => m.ItemId == i.Id).OrderBy(m => m.CreatedOn).FirstOrDefault();
                return new PreSchoolStudentStoreLineDto(i.Id, i.Name, i.InStock, g?.Id, g?.OnDate, g?.CreatedBy);
            })
            .ToList();

        return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Success(lines);
    }

    public async Task<Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>> GiveAsync(int studentId, int itemId, string? actor, CancellationToken ct = default)
    {
        var s = await _db.PreSchoolStudents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure("That child is not on the list.", ResultStatus.NotFound);

        var item = (await LoadItemsAsync(itemId, ct)).SingleOrDefault();
        if (item is null) return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure("That item no longer exists.", ResultStatus.NotFound);
        if (item.ClassId is not null && item.ClassId != s.ClassId)
            return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure($"{item.Name} is for {item.ClassName}, not this child's class.");
        if (await _db.PreSchoolStoreMovements.AnyAsync(m => m.StudentId == studentId && m.ItemId == itemId && m.Kind == Out, ct))
            return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure($"{s.ChildName} has already been given {item.Name}.", ResultStatus.Conflict);
        if (item.InStock <= 0)
            return Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>.Failure($"{item.Name} is out of stock. Enter the new stock on the Store screen first.");

        _db.PreSchoolStoreMovements.Add(new PreSchoolStoreMovement
        {
            ItemId = itemId, Kind = Out, Quantity = 1, StudentId = studentId,
            OnDate = DateOnly.FromDateTime(DateTime.Today), CreatedBy = actor, CreatedOn = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);

        return await ForStudentAsync(studentId, ct);
    }

    public async Task<Result<bool>> RemoveMovementAsync(int movementId, CancellationToken ct = default)
    {
        var m = await _db.PreSchoolStoreMovements.FirstOrDefaultAsync(x => x.Id == movementId, ct);
        if (m is null) return Result<bool>.Failure("That entry no longer exists.", ResultStatus.NotFound);

        if (m.Kind == In)
        {
            // Stock cannot go below what has already been given out.
            var stats = await _db.PreSchoolStoreMovements.Where(x => x.ItemId == m.ItemId)
                .GroupBy(x => x.Kind).Select(g => new { g.Key, Total = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            var received = stats.Where(x => x.Key == In).Sum(x => x.Total);
            var given = stats.Where(x => x.Key == Out).Sum(x => x.Total);
            if (received - m.Quantity < given)
                return Result<bool>.Failure("Removing this would leave less stock than has already been given out.");
        }

        _db.PreSchoolStoreMovements.Remove(m);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /* ---------------------------------------------------------- helpers */

    private async Task<List<PreSchoolStoreItemDto>> LoadItemsAsync(int? onlyId, CancellationToken ct)
    {
        var items = await _db.PreSchoolStoreItems.AsNoTracking().Include(i => i.Class)
            .Where(i => onlyId == null || i.Id == onlyId)
            .ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();

        var totals = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => ids.Contains(m.ItemId))
            .GroupBy(m => new { m.ItemId, m.Kind })
            .Select(g => new { g.Key.ItemId, g.Key.Kind, Total = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        var spent = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => ids.Contains(m.ItemId) && m.Kind == In && m.UnitCost != null)
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Amount = g.Sum(x => x.Quantity * x.UnitCost!.Value) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Amount, ct);

        // Active children per class, and per item those who already have it.
        var activeByClass = await _db.PreSchoolStudents.AsNoTracking()
            .Where(s => s.Status == PreSchoolStudentStatus.Active)
            .GroupBy(s => s.ClassId).Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClassId, x => x.Count, ct);
        var activeTotal = activeByClass.Values.Sum();

        var haveIt = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => ids.Contains(m.ItemId) && m.Kind == Out && m.Student!.Status == PreSchoolStudentStatus.Active)
            .GroupBy(m => m.ItemId).Select(g => new { ItemId = g.Key, Count = g.Select(x => x.StudentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        return items
            .OrderBy(i => i.Class == null ? int.MaxValue : i.Class.SortOrder).ThenBy(i => i.SortOrder).ThenBy(i => i.Name)
            .Select(i =>
            {
                var received = totals.Where(t => t.ItemId == i.Id && t.Kind == In).Sum(t => t.Total);
                var given = totals.Where(t => t.ItemId == i.Id && t.Kind == Out).Sum(t => t.Total);
                var students = i.ClassId is int c ? activeByClass.GetValueOrDefault(c) : activeTotal;
                return new PreSchoolStoreItemDto(
                    i.Id, i.Name, i.ClassId, i.Class?.Name ?? "All classes", i.SortOrder, i.IsActive,
                    received, given, received - given, students, Math.Max(0, students - haveIt.GetValueOrDefault(i.Id)),
                    i.UnitCost, i.ChargeHead, Math.Round(spent.GetValueOrDefault(i.Id), 2));
            })
            .ToList();
    }

    public async Task<Result<PreSchoolSupplierAccountDto>> AddSupplierPaymentAsync(PreSchoolSupplierPaymentRequest request, string? actor, CancellationToken ct = default)
    {
        _db.PreSchoolSupplierPayments.Add(new PreSchoolSupplierPayment
        {
            Supplier = DefaultSupplier,
            PaidOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);
        return Result<PreSchoolSupplierAccountDto>.Success(await AccountAsync(ct));
    }

    public async Task<Result<PreSchoolSupplierAccountDto>> RemoveSupplierPaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var p = await _db.PreSchoolSupplierPayments.FirstOrDefaultAsync(x => x.Id == paymentId, ct);
        if (p is null) return Result<PreSchoolSupplierAccountDto>.Failure("That payment no longer exists.", ResultStatus.NotFound);
        _db.PreSchoolSupplierPayments.Remove(p);
        await _db.SaveChangesAsync(ct);
        return Result<PreSchoolSupplierAccountDto>.Success(await AccountAsync(ct));
    }

    /// <summary>Bought (deliveries with a price), paid, the balance, and the payments newest first.</summary>
    private async Task<PreSchoolSupplierAccountDto> AccountAsync(CancellationToken ct)
    {
        var bought = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => m.Kind == In && m.UnitCost != null)
            .SumAsync(m => (decimal?)(m.Quantity * m.UnitCost!.Value), ct) ?? 0m;
        var payments = await _db.PreSchoolSupplierPayments.AsNoTracking()
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.Id)
            .ToListAsync(ct);
        var paid = payments.Sum(p => p.Amount);
        bought = Math.Round(bought, 2);
        return new PreSchoolSupplierAccountDto(DefaultSupplier, bought, paid, bought - paid,
            payments.Take(100).Select(p => new PreSchoolSupplierPaymentDto(p.Id, p.Supplier, p.PaidOn, p.Amount, p.Mode, p.Reference, p.Note, p.CreatedBy)).ToList());
    }

    /// <summary>The PreSchool buys its kits, garments and prospectus from My Chhota School.</summary>
    private const string DefaultSupplier = "My Chhota School";

    private async Task<string> PrefixAsync(CancellationToken ct) =>
        (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";
}
