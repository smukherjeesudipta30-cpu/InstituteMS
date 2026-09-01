using InstituteMS.Application.Common;

namespace InstituteMS.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks one enquiry as dealt with, so it leaves the dashboard and the badge
    /// count drops. Nothing is deleted — the message stays in <c>dbo.CusMessage</c>.
    /// </summary>
    Task<Result<bool>> MarkEnquiryHandledAsync(int id, CancellationToken ct = default);

    /// <summary>Dismisses one dashboard alert. Already-read is success, not an error.</summary>
    Task<Result<bool>> MarkNotificationReadAsync(int id, string? actor, CancellationToken ct = default);

    /// <summary>Clears the badge. Returns how many were marked.</summary>
    Task<Result<int>> MarkAllNotificationsReadAsync(string? actor, CancellationToken ct = default);
}
