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

    /// <summary>
    /// A branch that takes its fees into its own account, by branch name (as on
    /// the student record), e.g. "Champahati". A UPI ID set on the branch itself
    /// in the Branches screen comes first; a branch in neither place pays into
    /// <see cref="UpiId"/>.
    /// </summary>
    public Dictionary<string, BranchUpi> Branches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One branch's own UPI account.</summary>
public class BranchUpi
{
    public string? UpiId { get; set; }
    public string? PayeeName { get; set; }
}
