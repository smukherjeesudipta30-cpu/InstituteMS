using InstituteMS.Application.Common;

namespace InstituteMS.Application.Enquiries;

/// <summary>
/// The walk-in enquiry register and its follow-up report.
/// </summary>
public interface IEnquiryService
{
    Task<PagedResult<CourseEnquiryDto>> GetListAsync(
        CourseEnquiryQuery query, CancellationToken ct = default);

    Task<Result<CourseEnquiryDto>> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        CourseEnquirySaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(
        int id, CourseEnquirySaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Records a call: moves the status on, appends what was said, and sets the
    /// next date. Increments the follow-up count.
    /// </summary>
    Task<Result<bool>> RecordFollowUpAsync(
        int id, CourseEnquiryFollowUpRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>The report. Same filters as the register, plus the totals.</summary>
    Task<CourseEnquiryReportDto> GetReportAsync(
        CourseEnquiryQuery query, CancellationToken ct = default);
}
