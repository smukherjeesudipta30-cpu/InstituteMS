namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Settings for the yearly Drawing &amp; Dance competition portal.
/// </summary>
public class CompetitionOptions
{
    public const string SectionName = "Competition";

    /// <summary>
    /// Whether the competition banner and registration portal are shown at all.
    /// </summary>
    /// <remarks>
    /// The endpoints stay reachable when this is false, so the whole thing can be
    /// built and tested against the live server without a visitor seeing a
    /// half-finished portal — the owner asked plainly not to deploy it "as half
    /// cook". Turn it on once the dry run has been done.
    /// </remarks>
    public bool Enabled { get; set; }
}
