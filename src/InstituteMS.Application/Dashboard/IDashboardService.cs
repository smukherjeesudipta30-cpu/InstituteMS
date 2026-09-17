using InstituteMS.Application.Common;

namespace InstituteMS.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks one enquiry as dealt with, so it leaves the dashboard and the badge
    /// count drops. Nothing is deleted — the message stays in <c>dbo.CusMessage</c>.
    /// </summary>
    Task<Result<bool>> MarkEnquiryHandledAsync(int id, bool handled, CancellationToken ct = default);

    /// <summary>
    /// Enquiries from the public contact form, filtered by whether they have been
    /// read. This is how a message is found again after it leaves the dashboard --
    /// marking one read hides it there, it does not throw it away.
    /// </summary>
    Task<EnquiryListDto> GetEnquiriesAsync(
        EnquiryFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Dismisses one dashboard alert. Already-read is success, not an error.</summary>
    Task<Result<bool>> MarkNotificationReadAsync(int id, string? actor, CancellationToken ct = default);

    /// <summary>Clears the badge. Returns how many were marked.</summary>
    Task<Result<int>> MarkAllNotificationsReadAsync(string? actor, CancellationToken ct = default);
}
