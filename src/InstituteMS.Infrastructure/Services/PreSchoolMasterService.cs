using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The PreSchool's masters. Reads and writes PreSchool* tables only — nothing
/// here touches a Computer Centre table.
/// </summary>
/// <remarks>
/// Nothing is deleted: classes, subjects and fee heads are retired with
/// IsActive instead, because admissions, marks and receipts will point at them.
/// The grade table is the exception, being replaced whole on every save; a
/// report card keeps the grade it printed, not a link to the band.
/// </remarks>
public class PreSchoolMasterService : IPreSchoolMasterService
{
    private readonly AppDbContext _db;

    public PreSchoolMasterService(AppDbContext db) => _db = db;

    public async Task<PreSchoolOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var classes = await _db.PreSchoolClasses.AsNoTracking()
            .Include(c => c.Subjects)
            .Include(c => c.FeeHeads)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

        var bands = await _db.PreSchoolGradeBands.AsNoTracking()
            .OrderByDescending(b => b.MinPercent)
            .Select(b => new PreSchoolGradeBandDto(b.Id, b.MinPercent, b.Grade, b.Remark))
            .ToListAsync(ct);

        var settings = await _db.PreSchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        return new PreSchoolOverviewDto(ToDto(settings), classes.Select(ToDto).ToList(), bands);
    }

    /* ------------------------------------------------------------ classes */

    public async Task<Result<PreSchoolClassDto>> CreateClassAsync(PreSchoolClassSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await _db.PreSchoolClasses.AnyAsync(c => c.Name == name, ct))
            return Result<PreSchoolClassDto>.Failure($"There is already a class called {name}.", ResultStatus.Conflict);

        var entity = new PreSchoolClass { CreatedBy = actor, CreatedOn = DateTime.Now };
        Apply(entity, request);
        _db.PreSchoolClasses.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolClassDto>.Success(ToDto(entity));
    }

    public async Task<Result<PreSchoolClassDto>> UpdateClassAsync(int id, PreSchoolClassSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolClasses
            .Include(c => c.Subjects)
            .Include(c => c.FeeHeads)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return Result<PreSchoolClassDto>.Failure("That class no longer exists.", ResultStatus.NotFound);

        var name = request.Name.Trim();
        if (await _db.PreSchoolClasses.AnyAsync(c => c.Id != id && c.Name == name, ct))
            return Result<PreSchoolClassDto>.Failure($"There is already a class called {name}.", ResultStatus.Conflict);

        Apply(entity, request);
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolClassDto>.Success(ToDto(entity));
    }

    private static void Apply(PreSchoolClass entity, PreSchoolClassSaveRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.SortOrder = request.SortOrder;
        entity.AdmissionFee = request.AdmissionFee;
        entity.MinAgeMonths = request.MinAgeMonths;
        entity.MaxAgeMonths = request.MaxAgeMonths;
        entity.IsActive = request.IsActive;
    }

    /* ----------------------------------------------------------- subjects */

    public async Task<Result<PreSchoolSubjectDto>> CreateSubjectAsync(PreSchoolSubjectSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var problem = await CheckSubjectAsync(null, request, ct);
        if (problem is not null) return Result<PreSchoolSubjectDto>.Failure(problem.Value.Error, problem.Value.Status);

        var entity = new PreSchoolSubject { CreatedBy = actor, CreatedOn = DateTime.Now };
        Apply(entity, request);
        _db.PreSchoolSubjects.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolSubjectDto>.Success(ToDto(entity));
    }

    public async Task<Result<PreSchoolSubjectDto>> UpdateSubjectAsync(int id, PreSchoolSubjectSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolSubjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return Result<PreSchoolSubjectDto>.Failure("That subject no longer exists.", ResultStatus.NotFound);

        var problem = await CheckSubjectAsync(id, request, ct);
        if (problem is not null) return Result<PreSchoolSubjectDto>.Failure(problem.Value.Error, problem.Value.Status);

        Apply(entity, request);
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolSubjectDto>.Success(ToDto(entity));
    }

    private async Task<(string Error, ResultStatus Status)?> CheckSubjectAsync(int? id, PreSchoolSubjectSaveRequest request, CancellationToken ct)
    {
        if (!await _db.PreSchoolClasses.AnyAsync(c => c.Id == request.ClassId, ct))
            return ("That class no longer exists.", ResultStatus.NotFound);

        var name = request.Name.Trim();
        if (await _db.PreSchoolSubjects.AnyAsync(s => s.Id != id && s.ClassId == request.ClassId && s.Name == name, ct))
            return ($"This class already has a subject called {name}.", ResultStatus.Conflict);

        return null;
    }

    private static void Apply(PreSchoolSubject entity, PreSchoolSubjectSaveRequest request)
    {
        entity.ClassId = request.ClassId;
        entity.Name = request.Name.Trim();
        entity.MaxMarks = request.MaxMarks;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
    }

    /* ---------------------------------------------------------- fee heads */

    public async Task<Result<PreSchoolFeeHeadDto>> CreateFeeHeadAsync(PreSchoolFeeHeadSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var problem = await CheckFeeHeadAsync(null, request, ct);
        if (problem is not null) return Result<PreSchoolFeeHeadDto>.Failure(problem.Value.Error, problem.Value.Status);

        var entity = new PreSchoolFeeHead { CreatedBy = actor, CreatedOn = DateTime.Now };
        Apply(entity, request);
        _db.PreSchoolFeeHeads.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolFeeHeadDto>.Success(ToDto(entity));
    }

    public async Task<Result<PreSchoolFeeHeadDto>> UpdateFeeHeadAsync(int id, PreSchoolFeeHeadSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolFeeHeads.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (entity is null) return Result<PreSchoolFeeHeadDto>.Failure("That fee no longer exists.", ResultStatus.NotFound);

        var problem = await CheckFeeHeadAsync(id, request, ct);
        if (problem is not null) return Result<PreSchoolFeeHeadDto>.Failure(problem.Value.Error, problem.Value.Status);

        Apply(entity, request);
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolFeeHeadDto>.Success(ToDto(entity));
    }

    private async Task<(string Error, ResultStatus Status)?> CheckFeeHeadAsync(int? id, PreSchoolFeeHeadSaveRequest request, CancellationToken ct)
    {
        if (!await _db.PreSchoolClasses.AnyAsync(c => c.Id == request.ClassId, ct))
            return ("That class no longer exists.", ResultStatus.NotFound);

        var name = request.Name.Trim();
        if (await _db.PreSchoolFeeHeads.AnyAsync(f => f.Id != id && f.ClassId == request.ClassId && f.Name == name, ct))
            return ($"This class already has a fee called {name}.", ResultStatus.Conflict);

        return null;
    }

    private static void Apply(PreSchoolFeeHead entity, PreSchoolFeeHeadSaveRequest request)
    {
        entity.ClassId = request.ClassId;
        entity.Name = request.Name.Trim();
        entity.MonthlyAmount = request.MonthlyAmount;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
    }

    /* -------------------------------------------------------- grade bands */

    public async Task<Result<IReadOnlyList<PreSchoolGradeBandDto>>> SaveGradeBandsAsync(
        PreSchoolGradeBandsSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var bands = request.Bands
            .Select(b => new PreSchoolGradeBand
            {
                MinPercent = b.MinPercent,
                Grade = b.Grade.Trim(),
                Remark = string.IsNullOrWhiteSpace(b.Remark) ? null : b.Remark.Trim(),
                ModifiedBy = actor,
                ModifiedOn = now
            })
            .ToList();

        // Through the execution strategy: the context retries on transient
        // failures, and a retrying context refuses a transaction opened by hand.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Deleted before the new rows go in: Grade is unique, and a band
            // kept under the same grade would otherwise collide with itself.
            await _db.PreSchoolGradeBands.ExecuteDeleteAsync(ct);
            _db.PreSchoolGradeBands.AddRange(bands);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return Result<IReadOnlyList<PreSchoolGradeBandDto>>.Success(bands
            .OrderByDescending(b => b.MinPercent)
            .Select(b => new PreSchoolGradeBandDto(b.Id, b.MinPercent, b.Grade, b.Remark))
            .ToList());
    }

    /* ----------------------------------------------------------- settings */

    public async Task<Result<PreSchoolSettingDto>> SaveSettingsAsync(PreSchoolSettingSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (entity is null)
        {
            entity = new PreSchoolSetting { Id = 1 };
            _db.PreSchoolSettings.Add(entity);
        }

        entity.SchoolName = request.SchoolName.Trim();
        entity.Address = Clean(request.Address);
        entity.Phone = Clean(request.Phone);
        entity.Email = Clean(request.Email);
        entity.MapUrl = Clean(request.MapUrl);
        entity.UniformShopName = Clean(request.UniformShopName);
        entity.UniformShopAddress = Clean(request.UniformShopAddress);
        entity.UniformShopPhone = Clean(request.UniformShopPhone);
        entity.UniformShopMapUrl = Clean(request.UniformShopMapUrl);
        entity.ReceiptPrefix = request.ReceiptPrefix.Trim().ToUpperInvariant();
        entity.OpensOn = request.OpensOn;
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolSettingDto>.Success(ToDto(entity));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /* ------------------------------------------------------------ mapping */

    private static PreSchoolClassDto ToDto(PreSchoolClass c) => new(
        c.Id, c.Name, c.SortOrder, c.AdmissionFee, c.MinAgeMonths, c.MaxAgeMonths, c.IsActive,
        c.FeeHeads.Where(f => f.IsActive).Sum(f => f.MonthlyAmount),
        c.Subjects.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).Select(ToDto).ToList(),
        c.FeeHeads.OrderBy(f => f.SortOrder).ThenBy(f => f.Name).Select(ToDto).ToList());

    private static PreSchoolSubjectDto ToDto(PreSchoolSubject s) =>
        new(s.Id, s.ClassId, s.Name, s.MaxMarks, s.SortOrder, s.IsActive);

    private static PreSchoolFeeHeadDto ToDto(PreSchoolFeeHead f) =>
        new(f.Id, f.ClassId, f.Name, f.MonthlyAmount, f.SortOrder, f.IsActive);

    /// <summary>Before migration 086 has run there is no row; the screens still need a name to show.</summary>
    private static PreSchoolSettingDto ToDto(PreSchoolSetting? s) => s is null
        ? new PreSchoolSettingDto("Avijan PreSchool", null, null, null, null, null, null, null, null, "APS", null, null, null)
        : new PreSchoolSettingDto(
            s.SchoolName, s.Address, s.Phone, s.Email, s.MapUrl,
            s.UniformShopName, s.UniformShopAddress, s.UniformShopPhone, s.UniformShopMapUrl,
            s.ReceiptPrefix, s.OpensOn, s.ModifiedBy, s.ModifiedOn);
}
