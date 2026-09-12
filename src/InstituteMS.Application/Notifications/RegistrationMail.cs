namespace InstituteMS.Application.Notifications;

/// <summary>
/// SMTP settings, bound from the <c>Mail</c> section of configuration.
/// </summary>
/// <remarks>
/// <para>The defaults target <b>GoDaddy shared hosting's own relay</b>, which is
/// the only outbound mail path that works there. GoDaddy blocks connections to
/// external SMTP servers: every one of the legacy application's 26 registration
/// emails failed with</para>
///
/// <code>
/// SocketException: An attempt was made to access a socket in a way forbidden
/// by its access permissions 142.251.x.x
/// </code>
///
/// <para>— Google's address range, because it was pointed at smtp.gmail.com.
/// Gmail cannot be made to work from that host at any port; the relay is not a
/// preference, it is the only option.</para>
///
/// <para>The relay takes no credentials and no TLS. It accepts mail only from a
/// domain hosted on the same account, so <see cref="FromAddress"/> must stay on
/// avijan.in.</para>
/// </remarks>
public class MailOptions
{
    public const string SectionName = "Mail";

    /// <summary>
    /// Turn sending off without removing the configuration. When false the
    /// message is still logged, with status <c>SKIPPED</c>, so a quiet period is
    /// visible rather than looking like nothing ever happened.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>GoDaddy's local relay. No authentication, no SSL, port 25.</summary>
    public string Host { get; set; } = "relay-hosting.secureserver.net";

    public int Port { get; set; } = 25;

    /// <summary>The relay does not offer STARTTLS; leave false for GoDaddy.</summary>
    public bool EnableSsl { get; set; }

    /// <summary>Blank for the GoDaddy relay, which authenticates by source IP.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Must be a mailbox on the hosted domain or the relay refuses the message.
    /// </summary>
    public string FromAddress { get; set; } = "info@avijan.in";

    public string FromName { get; set; } = "Avijan Computer Center";

    /// <summary>
    /// Seconds to wait before giving up.
    ///
    /// <para>Kept deliberately short. The send is awaited so the attempt is logged
    /// before the response returns, which means a stalled mail server is time the
    /// person admitting a student sits and waits. Measured against an unreachable
    /// relay, a 15-second timeout made the admission take 22 seconds.</para>
    ///
    /// <para>GoDaddy's relay is on the same network and answers in milliseconds,
    /// so five seconds is generous when it is healthy and tolerable when it is
    /// not. If that is still too long, the send needs moving to a background
    /// queue rather than this number being raised.</para>
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Where the artwork in the message body is fetched from, with no trailing
    /// slash.
    ///
    /// <para>A mail client has no notion of the site's origin, so every image has
    /// to carry an absolute, publicly reachable URL. It is configuration rather
    /// than a constant because the address differs per environment, and a logo
    /// pointing at localhost would simply be a broken image in the student's
    /// inbox.</para>
    /// </summary>
    public string SiteBaseUrl { get; set; } = "https://www.avijan.in";

    /// <summary>
    /// Where notifications meant for the institute rather than the student are
    /// sent — currently the alert raised when a student changes their password.
    /// Falls back to <see cref="FromAddress"/> when it is not configured.
    /// </summary>
    public string? AdminNotifyAddress { get; set; }
}

/// <summary>What a newly admitted student is told.</summary>
public record RegistrationMailRequest(
    string RegistrationId,
    string StudentName,
    string? CourseName,

    /// <summary>The student's own address — where the message goes.</summary>
    string? PersonalEmail,

    /// <summary>The institute-issued portal sign-in, quoted in the message.</summary>
    string? PortalEmail,
    string? PortalPassword,

    /* The admission receipt, so the emailed copy carries the same figures as the
       printed one rather than a summary of it. */
    string? BillNo = null,
    decimal? AdmissionFees = null,
    string? AdmissionFeesInWords = null,
    DateOnly? DateOfJoining = null,
    DateOnly? NextDueDate = null,

    /* The same receipt as a PDF, attached to the message. The body is unchanged;
       this is an addition to it. Null when generation failed, which must not stop
       the mail going out. */
    byte[]? Pdf = null,
    string? PdfFileName = null);

/// <summary>
/// Sent to the administrator when a student changes their own portal password.
/// </summary>
/// <remarks>
/// <para><b>This message carries the student's chosen password in clear.</b> It was
/// asked for so the office can help a student who later forgets it. Two things
/// follow from that, and whoever maintains this should know both:</para>
///
/// <para>People reuse passwords. A student who types the password they also use for
/// their email or their bank has just put it in the institute's inbox, where it will
/// sit for as long as the mailbox does. That is a different thing from the
/// institute-issued password in <c>Student.Avijan_login_Password</c>, which the
/// institute chose itself and the student was told.</para>
///
/// <para>It also means the forced first-login reset no longer removes plain text
/// from the institute's reach — it moves it from the database to a mailbox. If that
/// is not wanted, drop <see cref="NewPassword"/> from this record and from the two
/// message builders; everything else here is still useful on its own.</para>
/// </remarks>
public record StudentPasswordChangeMailRequest(
    string StudentName,
    string? CourseName,
    string? RegistrationId,

    /// <summary>The portal sign-in the student uses.</summary>
    string? LoginId,

    /// <summary>The password the student has just chosen, in clear.</summary>
    string NewPassword,

    /// <summary>Institute local time, which is what the office works in.</summary>
    DateTime ChangedAt);

/// <summary>
/// A money receipt to be emailed to the student after a monthly fee is taken.
/// Mirrors <c>FeeReceiptDto</c>, which is what the printed receipt uses, so the
/// emailed copy and the paper copy cannot drift apart.
/// </summary>
public record FeeReceiptMailRequest(
    string RegistrationId,
    string StudentName,
    string? CourseName,

    /// <summary>The student's own address — where the message goes.</summary>
    string? PersonalEmail,

    string? BillNo,
    decimal? AmountPaid,
    string? AmountPaidInWords,
    decimal? NewDue,
    DateOnly? FeeDate,
    DateOnly? NextDueDate,

    /* The same receipt as a PDF, attached to the message. The body is unchanged;
       this is an addition to it. Null when generation failed, which must not stop
       the mail going out. */
    byte[]? Pdf = null,
    string? PdfFileName = null);

/// <summary>
/// Sends the welcome message on admission.
/// </summary>
/// <remarks>
/// Every implementation must be <b>non-throwing</b>. An admission is not allowed
/// to fail because a mail server was unreachable — the student is registered
/// either way and the failure is recorded in <c>dbo.MailLog_registration_time</c>,
/// exactly as the legacy application did.
/// </remarks>
/// <summary>The six-digit code a member of staff must type back to confirm a mark.</summary>
public record AttendanceCodeMailRequest(
    string StaffName,
    string ToEmail,
    string Code,
    string Direction,
    string? Branch,
    DateTime MarkedOn,
    int ValidMinutes);

/// <summary>The six-digit code sent to someone who has forgotten their password.</summary>
public record PasswordResetCodeMailRequest(
    string Name,
    string ToEmail,
    string Code,
    int ValidMinutes);

public interface IStudentMailer
{
    /// <summary>
    /// Sends an attendance confirmation code.
    /// </summary>
    /// <returns>
    /// False when it could not be sent — the caller tells the person on screen
    /// rather than leaving them waiting for an email that is not coming.
    /// </returns>
    Task<bool> SendAttendanceCodeAsync(
        AttendanceCodeMailRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// The admission confirmation: the portal sign-in, and a copy of the admission
    /// receipt so the student has the bill without waiting for the printed one.
    /// </summary>
    Task SendWelcomeAsync(
        RegistrationMailRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// A copy of the money receipt, sent when a monthly fee is taken — at the
    /// counter or by the student paying through the portal.
    /// </summary>
    Task SendFeeReceiptAsync(
        FeeReceiptMailRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Tells the administrator that a student has changed their portal password.
    /// Goes to the institute, never to the student.
    /// </summary>
    Task SendStudentPasswordChangeAsync(
        StudentPasswordChangeMailRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Sends the code that lets someone who cannot sign in set a new password
    /// without the old one.
    /// </summary>
    /// <returns>
    /// False when it could not be sent — the caller tells the person on screen
    /// rather than leaving them waiting for an email that is not coming.
    /// </returns>
    Task<bool> SendPasswordResetCodeAsync(
        PasswordResetCodeMailRequest request, string? actor, CancellationToken ct = default);
}
