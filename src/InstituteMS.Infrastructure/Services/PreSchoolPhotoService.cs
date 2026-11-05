using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// PreSchool photos: a child's and two guardians' for the ID cards, and the
/// landing page's classroom picture. Reads and writes dbo.PreSchoolPhoto only.
/// </summary>
/// <remarks>
/// The browser shrinks and crops a picture before sending it, so what arrives
/// is a small JPEG; the size limit here only stops anything that skipped that.
/// </remarks>
public class PreSchoolPhotoService : IPreSchoolPhotoService
{
    /// <summary>An ID-card photo arrives at about 40 KB; a classroom picture at a few hundred.</summary>
    private const int MaxStudentBytes = 600 * 1024;
    private const int MaxSiteBytes = 2 * 1024 * 1024;

    private static readonly string[] SiteSlots = [PreSchoolPhotoSlots.SmartClass];
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp"];

    private readonly AppDbContext _db;

    public PreSchoolPhotoService(AppDbContext db) => _db = db;

    public async Task<Result<PreSchoolStudentPhotosDto>> GetStudentPhotosAsync(int studentId, CancellationToken ct = default)
    {
        if (!await _db.PreSchoolStudents.AnyAsync(s => s.Id == studentId, ct))
            return Result<PreSchoolStudentPhotosDto>.Failure("That child is not on the list.", ResultStatus.NotFound);

        var photos = await _db.PreSchoolPhotos.AsNoTracking()
            .Where(p => p.StudentId == studentId)
            .ToListAsync(ct);

        string? Url(string slot) => photos.FirstOrDefault(p => p.Slot == slot) is { } p
            ? $"data:{p.ContentType};base64,{Convert.ToBase64String(p.Data)}"
            : null;

        return Result<PreSchoolStudentPhotosDto>.Success(new PreSchoolStudentPhotosDto(
            Url(PreSchoolPhotoSlots.Child), Url(PreSchoolPhotoSlots.Guardian1), Url(PreSchoolPhotoSlots.Guardian2)));
    }

    public async Task<Result<PreSchoolStudentPhotosDto>> SaveStudentPhotoAsync(
        int studentId, string slot, PreSchoolPhotoSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var known = PreSchoolPhotoSlots.ForStudent.FirstOrDefault(s => string.Equals(s, slot, StringComparison.OrdinalIgnoreCase));
        if (known is null) return Result<PreSchoolStudentPhotosDto>.Failure("The photo is for the child, Guardian1 or Guardian2.");
        if (!await _db.PreSchoolStudents.AnyAsync(s => s.Id == studentId, ct))
            return Result<PreSchoolStudentPhotosDto>.Failure("That child is not on the list.", ResultStatus.NotFound);

        var existing = await _db.PreSchoolPhotos.FirstOrDefaultAsync(p => p.StudentId == studentId && p.Slot == known, ct);
        var problem = await ApplyAsync(existing, request, MaxStudentBytes, actor, p => { p.StudentId = studentId; p.Slot = known; }, ct);
        if (problem is not null) return Result<PreSchoolStudentPhotosDto>.Failure(problem);

        return await GetStudentPhotosAsync(studentId, ct);
    }

    public async Task<(byte[] Data, string ContentType, DateTime UpdatedOn)?> GetSitePhotoAsync(string slot, CancellationToken ct = default)
    {
        var p = await _db.PreSchoolPhotos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudentId == null && x.Slot == slot, ct);
        return p is null ? null : (p.Data, p.ContentType, p.UpdatedOn);
    }

    public async Task<Result<DateTime?>> SaveSitePhotoAsync(string slot, PreSchoolPhotoSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var known = SiteSlots.FirstOrDefault(s => string.Equals(s, slot, StringComparison.OrdinalIgnoreCase));
        if (known is null) return Result<DateTime?>.Failure("Unknown picture.");

        var existing = await _db.PreSchoolPhotos.FirstOrDefaultAsync(p => p.StudentId == null && p.Slot == known, ct);
        var problem = await ApplyAsync(existing, request, MaxSiteBytes, actor, p => { p.StudentId = null; p.Slot = known; }, ct);
        if (problem is not null) return Result<DateTime?>.Failure(problem);

        return Result<DateTime?>.Success(string.IsNullOrWhiteSpace(request.DataUrl) ? null : existing?.UpdatedOn ?? DateTime.Now);
    }

    /// <summary>Saves, replaces or (for an empty data URL) removes one photo. Returns a problem, or null.</summary>
    private async Task<string?> ApplyAsync(
        PreSchoolPhoto? existing, PreSchoolPhotoSaveRequest request, int maxBytes, string? actor,
        Action<PreSchoolPhoto> place, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DataUrl))
        {
            if (existing is not null)
            {
                _db.PreSchoolPhotos.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            return null;
        }

        var (bytes, type) = Decode(request.DataUrl);
        if (bytes is null || type is null) return "The photo could not be read. Take it again or choose another picture.";
        if (bytes.Length > maxBytes) return $"The photo is too large (over {maxBytes / 1024} KB).";

        var photo = existing ?? new PreSchoolPhoto();
        place(photo);
        photo.Data = bytes;
        photo.ContentType = type;
        photo.UpdatedBy = actor;
        photo.UpdatedOn = DateTime.Now;
        if (existing is null) _db.PreSchoolPhotos.Add(photo);
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>"data:image/jpeg;base64,…" to its bytes, checked to really be the image type it says.</summary>
    private static (byte[]? Bytes, string? Type) Decode(string dataUrl)
    {
        const string marker = ";base64,";
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal)) return (null, null);
        var at = dataUrl.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return (null, null);

        var type = dataUrl[5..at].ToLowerInvariant();
        if (!AllowedTypes.Contains(type)) return (null, null);

        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(at + marker.Length)..]); }
        catch (FormatException) { return (null, null); }

        var looksRight = type switch
        {
            "image/jpeg" => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "image/webp" => bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[8] == 0x57 && bytes[9] == 0x45,
            _ => false
        };
        return looksRight ? (bytes, type) : (null, null);
    }
}
