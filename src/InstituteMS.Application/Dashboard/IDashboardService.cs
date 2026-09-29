using InstituteMS.Application.Common;

namespace InstituteMS.Application.Dashboard;

public interface IDashboardService
{
    /// <param name="cycleMonth">
    /// Any day in the month a pay cycle ends in (September 2026 = the 26 Aug to
    /// 25 Sep cycle); null for the cycle running today.
    /// </param>
    /// <param name="viewerIsSuperAdmin">
    /// An Editor's password-change alert carries their new password, so it is shown
    /// (and counted) for SuperAdmin only, never on an Admin's or Editor's dashboard.
    /// </param>
    Task<DashboardSummaryDto> GetSummaryAsync(
        DateOnly? cycleMonth = null, bool viewerIsSuperAdmin = false, CancellationToken ct = default);

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
    /// <param name="viewerIsSuperAdmin">An Editor's password-change alert is SuperAdmin's only.</param>
    Task<Result<bool>> MarkNotificationReadAsync(
        int id, string? actor, bool viewerIsSuperAdmin = false, CancellationToken ct = default);

    /// <summary>Clears the badge. Returns how many were marked — only alerts this viewer can see.</summary>
    Task<Result<int>> MarkAllNotificationsReadAsync(
        string? actor, bool viewerIsSuperAdmin = false, CancellationToken ct = default);
}
