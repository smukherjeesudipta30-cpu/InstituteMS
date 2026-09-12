namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Settings for the staff hierarchy chart.
/// </summary>
public class OrgOptions
{
    public const string SectionName = "Org";

    /// <summary>
    /// Whether the chart is shown on staff profiles, student profiles and the
    /// dashboard.
    /// </summary>
    /// <remarks>
    /// The endpoints stay reachable when this is false — they are role-guarded
    /// anyway — so the chart can be built and checked on the live server without
    /// anybody seeing it, which is what "before completion it will not disturb to
    /// site" means in practice. Turn it on once the hierarchy has been reviewed.
    /// </remarks>
    public bool Enabled { get; set; }
}
