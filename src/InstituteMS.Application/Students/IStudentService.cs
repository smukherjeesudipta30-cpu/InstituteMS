using InstituteMS.Application.Common;

namespace InstituteMS.Application.Students;

public interface IStudentService
{
    Task<PagedResult<StudentListItemDto>> GetListAsync(StudentQuery query, CancellationToken ct = default);

    Task<Result<StudentListItemDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Legacy <c>SearchDatRegistration</c>: exact registration id, ignoring active state
    /// so a completed student can still be pulled up.
    /// </summary>
    Task<Result<StudentListItemDto>> GetByRegistrationIdAsync(string registrationId, CancellationToken ct = default);

    Task<IReadOnlyList<StudentLookupDto>> GetLookupAsync(string? search, CancellationToken ct = default);

    Task<Result<StudentCreatedDto>> CreateAsync(StudentSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, StudentSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Soft delete: IsActive = 0 and DOE = today (replaces the legacy hard DELETE, defect S5).</summary>
    Task<Result<bool>> DeactivateAsync(int id, string? actor, CancellationToken ct = default);

    /// <summary>Legacy <c>ActivateStudent</c> — reactivates every student on that registration id.</summary>
    Task<Result<int>> ActivateAsync(string registrationId, string? actor, CancellationToken ct = default);

    Task<Result<AdmissionReceiptDto>> GetReceiptAsync(int id, CancellationToken ct = default);

    /// <param name="branchId">Optional — shapes the preview as <c>AVI/{code}/{n}/{yy}</c>.</param>
    Task<NextRegistrationDto> PeekNextRegistrationAsync(int? branchId, CancellationToken ct = default);

    /// <summary>
    /// The portal sign-in that would be issued for a student name, so the
    /// registration form can show it before the student is saved.
    /// </summary>
    Task<SuggestedLoginDto> SuggestLoginAsync(
        string? studentName, int? excludingStudentId = null, CancellationToken ct = default);

    Task<Result<(byte[] Content, string ContentType, string FileName)>> GetPhotoAsync(
        string registrationId, CancellationToken ct = default);

    /// <summary>
    /// Stores a student photo. Replaces <c>HomeController.SaveFiles</c> +
    /// <c>usp_UploadData</c>: same Base64-in-the-database behaviour, but with the
    /// size, MIME and magic-byte checks the legacy uploader never had (defect D10).
    /// </summary>
    Task<Result<StudentPhotoDto>> UploadPhotoAsync(
        string registrationId, PhotoUpload upload, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeletePhotoAsync(string registrationId, CancellationToken ct = default);

    /// <summary>
    /// What a student still owes on their admission fee, with the receipts taken
    /// against it so far.
    /// </summary>
    Task<Result<AdmissionDueDto>> GetAdmissionDueAsync(int studentId, CancellationToken ct = default);

    /// <summary>
    /// Takes an instalment against an outstanding admission fee: writes the receipt
    /// and moves the running total, in one transaction.
    /// </summary>
    Task<Result<AdmissionDueDto>> CollectAdmissionDueAsync(
        int studentId, AdmissionDueCollectRequest request, string? actor, CancellationToken ct = default);
}

/// <summary>An uploaded file, decoupled from ASP.NET's IFormFile.</summary>
public record PhotoUpload(string FileName, string? ContentType, long Length, Stream Content);

public record StudentPhotoDto(int FileId, string FileName, string ContentType, int FileSize);
