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
    public string Tagline { get; set; } = string.Empty;
    public string Introduction { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = [];
    public ContactOptions Contact { get; set; } = new();

    public class ContactOptions
    {
        public string AddressLine1 { get; set; } = string.Empty;
        public string AddressLine2 { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string OfficeHours { get; set; } = string.Empty;
    }
}
