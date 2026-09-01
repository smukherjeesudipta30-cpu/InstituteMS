namespace InstituteMS.Infrastructure.Settings;

/// <summary>
/// The institute's UPI details, shown beside the QR code a student pays against.
/// </summary>
/// <remarks>
/// Configuration rather than a database row because it identifies the institute's
/// own merchant account, not anything about a student — and because leaving
/// <see cref="UpiId"/> empty is how online payment stays switched off until the
/// office is ready for it.
/// </remarks>
public class PaymentSettings
{
    public const string SectionName = "Payments";

    /// <summary>e.g. <c>paytm.s1ngj29@pty</c>. Empty disables online payment.</summary>
    public string? UpiId { get; set; }

    /// <summary>The name shown on the payer's app.</summary>
    public string? PayeeName { get; set; }
}
