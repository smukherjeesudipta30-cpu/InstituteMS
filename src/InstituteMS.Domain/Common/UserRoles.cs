namespace InstituteMS.Domain.Common;

/// <summary>
/// Roles carried over from the legacy <c>dbo.Users.Roles</c> column.
///
/// MIGRATION_PLAN listed four (Admin, Editor, Student, Teacher), but the live data
/// also contains <c>Receptionist</c> and the combined <c>Receptionist &amp; Teacher</c>,
/// plus a <c>sudent</c> typo that the user migration corrects. A user can therefore
/// hold more than one role, which is why <see cref="Split"/> exists and why the JWT
/// carries one role claim per role.
/// </summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Receptionist = "Receptionist";

    /// <summary>
    /// Above every screen's own role list, not inside one: a SuperAdmin bypasses
    /// every <c>[Authorize(Roles=...)]</c> check globally (see
    /// <see cref="InstituteMS.Api.Security.SuperAdminAuthorizationHandler"/>)
    /// rather than being added to each of the ~27 controller role strings, so a
    /// screen that later tightens its own roles can't accidentally lock a
    /// SuperAdmin out by omission.
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    public static readonly string[] All = [SuperAdmin, Admin, Editor, Student, Teacher, Receptionist];

    /// <summary>Roles allowed into the back-office dashboard (legacy <c>[Authorize(Roles="Admin,Editor")]</c>).</summary>
    public const string BackOffice = $"{Admin},{Editor}";

    /// <summary>
    /// Who may work with the master data — students, teachers, receptionists,
    /// branches, courses, marks and notices. The receptionist runs the front desk,
    /// so these are the screens they spend the day in.
    /// </summary>
    /// <remarks>
    /// <para><b>Users is deliberately not a master.</b> The user screen creates
    /// sign-ins and assigns roles, so a receptionist with access to it could make
    /// themselves an administrator. It stays on <see cref="BackOffice"/>.</para>
    ///
    /// <para>Deleting also stays with the administrator on every one of these
    /// screens. Reading and entering is the receptionist's job; destroying a master
    /// row that reports and ledgers point at is not.</para>
    /// </remarks>
    public const string MasterData = $"{Admin},{Editor},{Receptionist}";

    /// <summary>Separators seen in the legacy column: "Receptionist &amp; Teacher", "A,B", "A/B".</summary>
    private static readonly char[] Separators = ['&', ',', '/', '+'];

    /// <summary>
    /// Ranked most privileged first. Used to pick the landing page when a user
    /// holds several roles.
    /// </summary>
    private static readonly string[] Precedence = [SuperAdmin, Admin, Editor, Receptionist, Teacher, Student];

    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Splits the stored value into individual roles, correcting casing and the
    /// legacy <c>sudent</c> typo. Unrecognised values are dropped rather than
    /// granting an unknown permission.
    /// </summary>
    public static string[] Split(string? roles)
    {
        if (string.IsNullOrWhiteSpace(roles)) return [];

        return roles
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Canonicalise)
            .Where(role => role is not null)
            .Select(role => role!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The single role that decides where the user lands after signing in.</summary>
    public static string Primary(string? roles)
    {
        var held = Split(roles);

        return Precedence.FirstOrDefault(held.Contains) ?? Student;
    }

    /// <summary>
    /// Landing route per role — mirrors <c>AccountController.Login</c>
    /// (AccountController.cs:75-140), which sent Admin and Editor to Home/Index,
    /// a Student to StudentReport/Index and a Teacher to TeacherReport/Index.
    /// </summary>
    /// <remarks>
    /// These must be routes that actually exist in the Angular app: the client
    /// navigates straight here after signing in, and an unknown path falls through
    /// the wildcard route to the public About page, which looks like a failed login.
    /// </remarks>
    public static string LandingRoute(string? roles) => Primary(roles) switch
    {
        SuperAdmin or Admin or Editor => "/dashboard",
        // The legacy Receptionist landed on Reception/Resignation_portal, which is
        // not migrated. Course progress is the one page they were authorised for.
        Receptionist => "/courses",
        Teacher => "/reports/teacher-earnings",
        Student => "/reports/student-statement",
        _ => "/dashboard"
    };

    private static string? Canonicalise(string value) => value.Trim().ToLowerInvariant() switch
    {
        "superadmin" or "super admin" or "super-admin" => SuperAdmin,
        "admin" => Admin,
        "editor" => Editor,
        "teacher" => Teacher,
        "receptionist" => Receptionist,
        // The legacy data contains a 'sudent' typo on one account.
        "student" or "sudent" => Student,
        _ => null
    };
}
