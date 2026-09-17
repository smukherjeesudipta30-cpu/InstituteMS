namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Editable copy for the public About Us page. Bound from the
/// <c>InstituteProfile</c> section of appsettings.json so the content can be
/// changed without a redeploy of the Angular app.
/// </summary>
public class InstituteProfileOptions
{
    public const string SectionName = "InstituteProfile";

    public string Name { get; set; } = "Avijan";

    /// <summary>
    /// The year the institute opened, which is what "years of teaching" counts from.
    /// </summary>
    /// <remarks>
    /// Configured rather than read from the data, and it is the one headline figure
    /// that is not live. It used to be worked out from the earliest student on file,
    /// but the records only reach back to December 2016 — the migration brought
    /// across what the old system held, not the whole history — so the front page
    /// claimed "10+ years" for a centre that has been teaching since 2008. Counting
    /// from a stated year is both true and self-maintaining: it turns over on its
    /// own each January.
    /// </remarks>
    public int FoundedYear { get; set; } = 2008;

    public string Tagline { get; set; } = string.Empty;
    public string Introduction { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = [];
    public ContactOptions Contact { get; set; } = new();
    public OwnerDeskOptions OwnerDesk { get; set; } = new();

    /// <summary>
    /// The "From the Owner's Desk" panel on the front page. Left out of the reply
    /// entirely when <see cref="OwnerDeskOptions.Photo"/> is blank, so the section
    /// never renders as an empty frame.
    /// </summary>
    public class OwnerDeskOptions
    {
        /// <summary>Blank until the owner confirms how they wish to be named publicly.</summary>
        public string Name { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Photo { get; set; } = string.Empty;

        /// <summary>One paragraph per entry.</summary>
        public List<string> Message { get; set; } = [];

        /// <summary>Short line under the signature, e.g. the year the centre opened.</summary>
        public string Since { get; set; } = string.Empty;
    }

    public class ContactOptions
    {
        public string AddressLine1 { get; set; } = string.Empty;
        public string AddressLine2 { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string OfficeHours { get; set; } = string.Empty;
    }
}
