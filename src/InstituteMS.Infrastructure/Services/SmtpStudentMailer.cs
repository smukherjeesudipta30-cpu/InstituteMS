using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using InstituteMS.Application.Notifications;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The two messages a student is sent: the admission confirmation, and a copy of
/// the money receipt each time a monthly fee is taken. Every attempt is recorded
/// in <c>dbo.MailLog_registration_time</c>.
/// </summary>
/// <remarks>
/// <para><b>This class never throws.</b> A student who has been admitted stays
/// admitted, and a fee that has been taken stays taken, whether or not the mail
/// server answered. The legacy application got this right and it matters more here
/// than it looks: all 26 of its production sends failed, and had a failure aborted
/// the admission, no student would have been registered for two months.</para>
///
/// <para>Since the move off GoDaddy this sends through Gmail, authenticated with a
/// Google App Password. The defaults in <see cref="MailOptions"/> still describe the
/// GoDaddy relay, which is what the legacy host required; the live settings come
/// from the <c>Mail</c> section of configuration and override them.</para>
/// </remarks>
public class SmtpStudentMailer : IStudentMailer
{
    private readonly AppDbContext _db;
    private readonly MailOptions _options;
    private readonly ILogger<SmtpStudentMailer> _logger;

    public SmtpStudentMailer(
        AppDbContext db,
        IOptions<MailOptions> options,
        ILogger<SmtpStudentMailer> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /* ------------------------------------------------------------- messages */

    public Task SendWelcomeAsync(
        RegistrationMailRequest r, string? actor, CancellationToken ct = default) =>
        SendAsync(
            to: r.PersonalEmail,
            subject: "Welcome to Avijan Computer Center",
            html: WelcomeHtml(r),
            text: WelcomeText(r),
            logReference: r.PortalEmail ?? r.RegistrationId,
            what: $"admission {r.RegistrationId}",
            pdf: r.Pdf,
            pdfFileName: r.PdfFileName,
            actor: actor,
            ct: ct);

    public Task SendFeeReceiptAsync(
        FeeReceiptMailRequest r, string? actor, CancellationToken ct = default) =>
        SendAsync(
            to: r.PersonalEmail,
            subject: $"Avijan - Money receipt {r.BillNo}",
            html: FeeReceiptHtml(r),
            text: FeeReceiptText(r),
            logReference: r.BillNo ?? r.RegistrationId,
            what: $"receipt {r.BillNo}",
            pdf: r.Pdf,
            pdfFileName: r.PdfFileName,
            actor: actor,
            ct: ct);

    public Task SendStudentPasswordChangeAsync(
        StudentPasswordChangeMailRequest r, string? actor, CancellationToken ct = default) =>
        SendAsync(
            // To the institute, never to the student - this one carries their password.
            to: string.IsNullOrWhiteSpace(_options.AdminNotifyAddress)
                ? _options.FromAddress
                : _options.AdminNotifyAddress,
            subject: $"Password changed - {r.StudentName} ({r.RegistrationId})",
            html: PasswordChangeHtml(r),
            text: PasswordChangeText(r),
            logReference: r.RegistrationId ?? r.LoginId ?? r.StudentName,
            what: $"password change {r.RegistrationId}",
            pdf: null,
            pdfFileName: null,
            actor: actor,
            ct: ct);

    /// <summary>
    /// The attendance code. Unlike every other message here, the caller needs to
    /// know whether it actually went — a person waiting at the door for a code that
    /// silently failed has no way to mark their attendance.
    /// </summary>
    public async Task<bool> SendAttendanceCodeAsync(
        AttendanceCodeMailRequest r, string? actor, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;
        if (string.IsNullOrWhiteSpace(r.ToEmail)) return false;

        try
        {
            await SendAsync(
                /* The code is deliberately absent from the subject and from what is
                   written to the mail log. Putting it in the subject was convenient
                   for testing and quietly wrong: the log is readable by anyone with
                   database access, and a live code there is enough to mark a
                   colleague present. */
                to: r.ToEmail,
                subject: "Avijan attendance code",
                html: AttendanceCodeHtml(r),
                text: AttendanceCodeText(r),
                logReference: r.ToEmail,
                what: $"attendance code for {r.StaffName}",
                pdf: null,
                pdfFileName: null,
                actor: actor,
                ct: ct,
                logBody: $"Attendance code for {r.StaffName} — code not recorded.");

            /* SendAsync never throws — it logs and moves on, which is right for a
               receipt but not here. The log row it wrote is the only record of
               success, so it is read back rather than assumed. */
            var sent = await _db.MailLog
                .AsNoTracking()
                .Where(m => m.ToEmail == r.ToEmail)
                .OrderByDescending(m => m.Id)
                .Select(m => m.Status)
                .FirstOrDefaultAsync(ct);

            return string.Equals(sent, "SENT", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Attendance code could not be sent to {Email}.", r.ToEmail);
            return false;
        }
    }

    /// <summary>
    /// The password reset code. Same shape as <see cref="SendAttendanceCodeAsync"/> —
    /// the caller needs to know whether it actually went, and the code itself is kept
    /// out of the mail log for the same reason an attendance code is.
    /// </summary>
    public async Task<bool> SendPasswordResetCodeAsync(
        PasswordResetCodeMailRequest r, string? actor, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;
        if (string.IsNullOrWhiteSpace(r.ToEmail)) return false;

        try
        {
            await SendAsync(
                to: r.ToEmail,
                subject: "Avijan password reset code",
                html: PasswordResetCodeHtml(r),
                text: PasswordResetCodeText(r),
                logReference: r.ToEmail,
                what: $"password reset code for {r.Name}",
                pdf: null,
                pdfFileName: null,
                actor: actor,
                ct: ct,
                logBody: $"Password reset code for {r.Name} — code not recorded.");

            var sent = await _db.MailLog
                .AsNoTracking()
                .Where(m => m.ToEmail == r.ToEmail)
                .OrderByDescending(m => m.Id)
                .Select(m => m.Status)
                .FirstOrDefaultAsync(ct);

            return string.Equals(sent, "SENT", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Password reset code could not be sent to {Email}.", r.ToEmail);
            return false;
        }
    }

    /* ---------------------------------------------------------------- send */

    private async Task SendAsync(
        string? to, string subject, string html, string text,
        string logReference, string what,
        byte[]? pdf, string? pdfFileName,
        string? actor, CancellationToken ct,
        // What to keep in the mail log instead of the real body. An attendance code
        // must not be readable from dbo.MailLog_registration_time: anyone with
        // database access could then mark a colleague present.
        string? logBody = null)
    {
        /* Disabled means DORMANT, not "log that nothing happened". Checked before
           anything else so a switched-off mailer writes no rows at all and an
           admission behaves exactly as it did before this class existed. */
        if (!_options.Enabled) return;

        var recipient = to?.Trim();

        /* No address is a data problem, not a mail problem, and it is worth
           seeing: ten of the legacy application's twenty-six attempts had a blank
           recipient. Personal email is required on a new admission, so this should
           now only appear for students admitted before that rule. */
        if (string.IsNullOrWhiteSpace(recipient))
        {
            await LogAsync(logReference, string.Empty, subject, logBody ?? html, "SKIPPED",
                "No personal email on the student record.", actor, ct);
            return;
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Timeout = _options.TimeoutSeconds * 1000,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            /* The GoDaddy relay authenticates by source IP and rejects a login
               attempt, so credentials are sent only when configured. */
            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject
            };

            /* Sent as multipart/alternative rather than HTML alone. A message that
               is only HTML, carries credentials and comes from an automated sender
               is exactly the shape spam filters distrust, and the few clients that
               refuse HTML outright would otherwise show the student nothing. */
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(text, null, "text/plain"));
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(html, null, "text/html"));

            message.To.Add(recipient);

            /* The receipt as a PDF, alongside the unchanged body. Held in a using so
               the stream is closed after the send, and skipped silently when the
               generator produced nothing — a receipt that reaches the student
               without its attachment beats no receipt at all. */
            using var attachment = pdf is { Length: > 0 }
                ? new Attachment(new MemoryStream(pdf), pdfFileName ?? "receipt.pdf", "application/pdf")
                : null;

            if (attachment is not null) message.Attachments.Add(attachment);

            await client.SendMailAsync(message, ct);
            await LogAsync(logReference, recipient, subject, logBody ?? html, "SENT", null, actor, ct);

            _logger.LogInformation("Mail sent to {Email} for {What}", recipient, what);
        }
        catch (Exception ex)
        {
            /* Deliberately broad. Anything at all going wrong here - a blocked
               socket, a refused relay, a malformed address - must not surface to
               the caller, because the caller has already committed the admission
               or the payment. */
            await LogAsync(logReference, recipient, subject, logBody ?? html, "FAILED", Describe(ex), actor, ct);

            _logger.LogError(ex,
                "Mail FAILED for {What} to {Email}. The underlying record was not affected.",
                what, recipient);
        }
    }

    /// <summary>
    /// The exception and its inner causes, which is where the useful part lives:
    /// "Failure sending mail" alone says nothing, while the inner
    /// <c>SocketException</c> is what identifies a blocked relay.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();

        for (Exception? e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().FullName}: {e.Message}");

        return string.Join(" ---> ", parts);
    }

    private async Task LogAsync(
        string reference, string to, string subject, string body,
        string status, string? error, string? actor, CancellationToken ct)
    {
        try
        {
            _db.MailLog.Add(new MailLogEntry
            {
                ForRegistrationId = reference,
                ToEmail = to,
                Subject = subject,
                MailBody = body,
                Status = status,
                ErrorMessage = error,
                CreatedDate = DateTime.Now,
                CreatedBy = actor
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Even the logging must not be able to break an admission or a payment.
            _logger.LogError(ex, "Could not write the mail log for {Reference}", reference);
        }
    }

    /* ------------------------------------------------------------ rendering */

    /* Brand colours, kept together so the message and the institute's own
       stationery can be changed in one place. */
    private const string Blue = "#0d6efd";
    private const string Ink = "#212529";
    private const string Muted = "#6b7280";
    private const string Line = "#e3e6ea";
    private const string Wash = "#f8f9fa";

    /// <summary>
    /// Stated on both the emailed and the printed copy. These receipts are issued
    /// by the system and never signed, so the note is what tells a student the
    /// blank space is intentional rather than an unfinished document.
    /// </summary>
    private const string ComputerGenerated =
        "This is a computer generated bill. No signature is required.";

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Plain digits with Indian grouping - the legacy receipt printed no symbol.</summary>
    private static string Rupees(decimal? value) =>
        (value ?? 0m).ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));

    private static string ShortDate(DateOnly? value) =>
        value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

    /// <summary>
    /// The branded shell both messages sit in: blue masthead with the Avijan mark,
    /// the content, then the institute's address.
    /// </summary>
    /// <remarks>
    /// <para>Nested tables with every rule inline, which is the only construction
    /// that survives an email client. Gmail and Outlook both discard a
    /// <c>&lt;style&gt;</c> block, and Outlook renders through Word, which has no
    /// flexbox and no grid - so a stylesheet-and-divs layout that looks right in a
    /// browser arrives as a stack of unstyled text.</para>
    ///
    /// <para>The logo is an absolute URL because a mail client has no base URL to
    /// resolve a relative path against. Some clients ask before showing remote
    /// images, so nothing that matters is carried by the picture alone - the
    /// masthead reads correctly with images turned off.</para>
    /// </remarks>
    private string Shell(string subtitle, string content) => $"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background:#f4f5f7;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                 style="background:#f4f5f7;padding:24px 12px;">
            <tr><td align="center">
              <table role="presentation" width="640" cellpadding="0" cellspacing="0"
                     style="width:100%;max-width:640px;background:#ffffff;border-collapse:collapse;
                            font-family:Arial,Helvetica,sans-serif;">

                <tr>
                  <td style="background:{Blue};padding:22px 28px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td style="color:#ffffff;font-size:24px;font-weight:bold;line-height:30px;">
                          Avijan Computer Center
                          <div style="font-size:13px;font-weight:normal;padding-top:6px;opacity:0.92;">
                            {Escape(subtitle)}
                          </div>
                        </td>
                        <td align="right" width="160" style="width:160px;">
                          <img src="{_options.SiteBaseUrl.TrimEnd('/')}/images/logo.png" width="150" alt="Avijan"
                               style="display:block;border:0;outline:none;width:150px;height:auto;">
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>

                <tr><td style="padding:28px;color:{Ink};">{content}</td></tr>

                <tr>
                  <td style="background:{Wash};border-top:1px solid {Line};padding:18px 28px;
                             font-size:12px;line-height:19px;color:{Muted};">
                    <strong style="color:{Ink};">Avijan Computer Center</strong><br>
                    53, Aghore Sarani, Rajpur, Kol-149 &nbsp;&middot;&nbsp; Mob.: 7688045949<br>
                    <span style="color:{Muted};">This message was sent automatically. Please do not reply to it.</span>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body></html>
        """;

    /// <summary>A bordered panel with a blue heading, as on the printed stationery.</summary>
    private static string Card(string title, string rows) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
               style="border:1px solid {Line};border-radius:4px;margin:0 0 22px;border-collapse:separate;">
          <tr>
            <td style="padding:14px 16px 10px;font-size:15px;font-weight:bold;color:{Blue};">{title}</td>
          </tr>
          <tr>
            <td style="padding:0 16px 16px;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                     style="border-collapse:collapse;font-size:14px;">
                {rows}
              </table>
            </td>
          </tr>
        </table>
        """;

    /// <summary>One label/value line inside a <see cref="Card"/>.</summary>
    private static string Row(string label, string valueHtml) => $"""
        <tr>
          <td width="40%" style="width:40%;background:{Wash};border:1px solid {Line};
                                 padding:10px 12px;font-weight:bold;color:{Ink};">{label}</td>
          <td style="border:1px solid {Line};padding:10px 12px;color:{Ink};">{valueHtml}</td>
        </tr>
        """;

    private static string Paragraph(string html) =>
        $"""<p style="margin:0 0 14px;font-size:14px;line-height:22px;">{html}</p>""";

    private static string Note(string text) => $"""
        <p style="margin:22px 0 0;font-size:12px;line-height:19px;color:{Muted};font-style:italic;">
          {Escape(text)}
        </p>
        """;

    /* ------------------------------------------------- admission confirmation */

    private string WelcomeHtml(RegistrationMailRequest r)
    {
        // Omitted rather than shown empty when a student has no portal account, so
        // the message never promises credentials it has not got.
        var login = string.IsNullOrWhiteSpace(r.PortalEmail)
            ? string.Empty
            : Card("Student Login Details",
                  Row("Login ID", $"""<a href="mailto:{Escape(r.PortalEmail)}" style="color:{Blue};">{Escape(r.PortalEmail)}</a>""") +
                  Row("Password", Escape(r.PortalPassword ?? "(as given to you)"))) +
              Paragraph("Please change this password after you sign in for the first time.");

        var receiptRows =
            Row("Bill No", Escape(r.BillNo)) +
            Row("Registration Number", Escape(r.RegistrationId)) +
            (string.IsNullOrWhiteSpace(r.CourseName) ? string.Empty : Row("Course", Escape(r.CourseName))) +
            Row("Date of Joining", ShortDate(r.DateOfJoining)) +
            Row("Admission Fees", Rupees(r.AdmissionFees)) +
            (string.IsNullOrWhiteSpace(r.AdmissionFeesInWords)
                ? string.Empty
                : Row("In Words", $"{Escape(r.AdmissionFeesInWords)} Only")) +
            Row("Next Payment Due", ShortDate(r.NextDueDate));

        return Shell("Student Registration Confirmation", $"""
            <p style="margin:0 0 16px;font-size:17px;font-weight:bold;">Hello {Escape(r.StudentName)},</p>
            {Paragraph("Your registration has been completed successfully.")}
            <p style="margin:0 0 22px;font-size:14px;line-height:22px;">
              Thank you for joining <strong>Avijan Computer Center</strong>. We are delighted to
              have you with us and wish you success in your learning journey.
            </p>
            {login}
            {Card("Admission Receipt", receiptRows)}
            {Paragraph("Please keep your registration number safe - it is needed for fees, admit cards and certificates.")}
            {Note(ComputerGenerated)}
            """);
    }

    private static string WelcomeText(RegistrationMailRequest r)
    {
        var text = new StringBuilder()
            .AppendLine("AVIJAN COMPUTER CENTER")
            .AppendLine("Student Registration Confirmation")
            .AppendLine()
            .AppendLine($"Hello {r.StudentName},")
            .AppendLine()
            .AppendLine("Your registration has been completed successfully.")
            .AppendLine()
            .AppendLine("Thank you for joining Avijan Computer Center. We are delighted to have")
            .AppendLine("you with us and wish you success in your learning journey.")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(r.PortalEmail))
        {
            text.AppendLine("STUDENT LOGIN DETAILS")
                .AppendLine($"  Login ID : {r.PortalEmail}")
                .AppendLine($"  Password : {r.PortalPassword ?? "(as given to you)"}")
                .AppendLine()
                .AppendLine("Please change this password after you sign in for the first time.")
                .AppendLine();
        }

        text.AppendLine("ADMISSION RECEIPT")
            .AppendLine($"  Bill No             : {r.BillNo}")
            .AppendLine($"  Registration Number : {r.RegistrationId}");

        if (!string.IsNullOrWhiteSpace(r.CourseName))
            text.AppendLine($"  Course              : {r.CourseName}");

        text.AppendLine($"  Date of Joining     : {ShortDate(r.DateOfJoining)}")
            .AppendLine($"  Admission Fees      : {Rupees(r.AdmissionFees)}");

        if (!string.IsNullOrWhiteSpace(r.AdmissionFeesInWords))
            text.AppendLine($"  In Words            : {r.AdmissionFeesInWords} Only");

        return text
            .AppendLine($"  Next Payment Due    : {ShortDate(r.NextDueDate)}")
            .AppendLine()
            .AppendLine("Please keep your registration number safe - it is needed for fees,")
            .AppendLine("admit cards and certificates.")
            .AppendLine()
            .AppendLine(ComputerGenerated)
            .AppendLine()
            .AppendLine("--")
            .AppendLine("Avijan Computer Center")
            .AppendLine("53, Aghore Sarani, Rajpur, Kol-149 | Mob.: 7688045949")
            .AppendLine("This message was sent automatically. Please do not reply to it.")
            .ToString();
    }

    /* ------------------------------------------- password change (to admin) */

    private string PasswordChangeHtml(StudentPasswordChangeMailRequest r)
    {
        var when = r.ChangedAt.ToString("dd MMM yyyy 'at' hh:mm tt", CultureInfo.InvariantCulture);

        var rows =
            Row("Student Name", Escape(r.StudentName)) +
            Row("Registration Id", Escape(r.RegistrationId)) +
            (string.IsNullOrWhiteSpace(r.CourseName) ? string.Empty : Row("Course", Escape(r.CourseName))) +
            Row("Changed On", Escape(when)) +
            Row("User Name", Escape(r.LoginId)) +
            Row("Password", $"""<span style="font-family:Consolas,monospace;">{Escape(r.NewPassword)}</span>""");

        return Shell("Student Password Change", $"""
            <p style="margin:0 0 16px;font-size:17px;font-weight:bold;">Password changed</p>
            {Paragraph("A student has set a new password for the Avijan student portal. The details are recorded below.")}
            {Card("Student Details", rows)}
            <p style="margin:0;font-size:12px;line-height:19px;color:{Muted};font-style:italic;">
              This notice is for the institute only. It contains the student's password
              in readable form, so treat this mailbox accordingly.
            </p>
            """);
    }

    private static string PasswordChangeText(StudentPasswordChangeMailRequest r)
    {
        var when = r.ChangedAt.ToString("dd MMM yyyy 'at' hh:mm tt", CultureInfo.InvariantCulture);

        var text = new StringBuilder()
            .AppendLine("AVIJAN COMPUTER CENTER")
            .AppendLine("Student Password Change")
            .AppendLine()
            .AppendLine("A student has set a new password for the Avijan student portal.")
            .AppendLine()
            .AppendLine($"  Student Name    : {r.StudentName}")
            .AppendLine($"  Registration Id : {r.RegistrationId}");

        if (!string.IsNullOrWhiteSpace(r.CourseName))
            text.AppendLine($"  Course          : {r.CourseName}");

        return text
            .AppendLine($"  Changed On      : {when}")
            .AppendLine($"  User Name       : {r.LoginId}")
            .AppendLine($"  Password        : {r.NewPassword}")
            .AppendLine()
            .AppendLine("This notice is for the institute only. It contains the student's")
            .AppendLine("password in readable form, so treat this mailbox accordingly.")
            .AppendLine()
            .AppendLine("--")
            .AppendLine("Avijan Computer Center")
            .ToString();
    }

    /* --------------------------------------------------------- money receipt */

    private string FeeReceiptHtml(FeeReceiptMailRequest r)
    {
        var rows =
            Row("Bill No", Escape(r.BillNo)) +
            Row("Registration Number", Escape(r.RegistrationId)) +
            (string.IsNullOrWhiteSpace(r.CourseName) ? string.Empty : Row("Purpose", Escape(r.CourseName))) +
            Row("Date", ShortDate(r.FeeDate)) +
            Row("Amount Paid", Rupees(r.AmountPaid)) +
            (string.IsNullOrWhiteSpace(r.AmountPaidInWords)
                ? string.Empty
                : Row("In Words", $"{Escape(r.AmountPaidInWords)} Only")) +
            Row("Total Dues Until Today", Rupees(r.NewDue));

        // The legacy receipt carried this warning at its foot; students act on it,
        // so the emailed copy carries it too.
        var dueWarning = r.NextDueDate is null
            ? string.Empty
            : $"""
               <p style="margin:0 0 8px;font-size:14px;line-height:22px;font-weight:bold;color:{Ink};">
                 Please clear your next month's fees by {ShortDate(r.NextDueDate)} to avoid a fine.
               </p>
               """;

        return Shell("Money Receipt", $"""
            <p style="margin:0 0 16px;font-size:17px;font-weight:bold;">Hello {Escape(r.StudentName)},</p>
            {Paragraph("We have received your fee payment. Your receipt is below.")}
            {Card("Money Receipt", rows)}
            {dueWarning}
            {Note(ComputerGenerated)}
            """);
    }

    private static string FeeReceiptText(FeeReceiptMailRequest r)
    {
        var text = new StringBuilder()
            .AppendLine("AVIJAN COMPUTER CENTER")
            .AppendLine("Money Receipt")
            .AppendLine()
            .AppendLine($"Hello {r.StudentName},")
            .AppendLine()
            .AppendLine("We have received your fee payment. Your receipt is below.")
            .AppendLine()
            .AppendLine("MONEY RECEIPT")
            .AppendLine($"  Bill No                : {r.BillNo}")
            .AppendLine($"  Registration Number    : {r.RegistrationId}");

        if (!string.IsNullOrWhiteSpace(r.CourseName))
            text.AppendLine($"  Purpose                : {r.CourseName}");

        text.AppendLine($"  Date                   : {ShortDate(r.FeeDate)}")
            .AppendLine($"  Amount Paid            : {Rupees(r.AmountPaid)}");

        if (!string.IsNullOrWhiteSpace(r.AmountPaidInWords))
            text.AppendLine($"  In Words               : {r.AmountPaidInWords} Only");

        text.AppendLine($"  Total Dues Until Today : {Rupees(r.NewDue)}")
            .AppendLine();

        if (r.NextDueDate is not null)
        {
            text.AppendLine($"Please clear your next month's fees by {ShortDate(r.NextDueDate)} to avoid a fine.")
                .AppendLine();
        }

        return text
            .AppendLine(ComputerGenerated)
            .AppendLine()
            .AppendLine("--")
            .AppendLine("Avijan Computer Center")
            .AppendLine("53, Aghore Sarani, Rajpur, Kol-149 | Mob.: 7688045949")
            .AppendLine("This message was sent automatically. Please do not reply to it.")
            .ToString();
    }

    /* ------------------------------------------------- attendance code */

    /// <summary>
    /// Deliberately plain: the code has to be readable in a notification preview on
    /// a locked phone, so it goes near the top and is set large.
    /// </summary>
    private static string AttendanceCodeHtml(AttendanceCodeMailRequest r)
    {
        var word = string.Equals(r.Direction, "In", StringComparison.OrdinalIgnoreCase)
            ? "arrival"
            : "departure";

        // Formatted first: a ':' inside an interpolation hole in a raw string reads
        // as a format specifier and breaks the surrounding literal.
        var at = r.MarkedOn.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var on = r.MarkedOn.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        var school = System.Net.WebUtility.HtmlEncode(r.School);
        // The Computer Centre's footer reads exactly as it always has.
        var footer = System.Net.WebUtility.HtmlEncode(r.FooterLine).Replace(" | ", " &nbsp;|&nbsp; ");

        return $$"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6f9;padding:24px 0;">
          <tr><td align="center">
            <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;padding:28px;font-family:Arial,Helvetica,sans-serif;color:#212529;">
              <tr><td style="font-size:12px;letter-spacing:.12em;color:#6b7280;text-transform:uppercase;">{{school}}</td></tr>
              <tr><td style="padding-top:6px;font-size:18px;font-weight:bold;color:#1b5e8c;">Attendance code</td></tr>
              <tr><td style="padding-top:16px;font-size:15px;">Hello {{r.StaffName}},</td></tr>
              <tr><td style="padding-top:8px;font-size:15px;line-height:1.6;">
                Type this code to confirm your <strong>{{word}}</strong> at
                {{at}} on {{on}}.
              </td></tr>
              <tr><td align="center" style="padding:22px 0;">
                <div style="display:inline-block;background:#0a283c;color:#ffffff;font-size:34px;letter-spacing:.28em;font-weight:bold;padding:14px 26px;border-radius:10px;">{{r.Code}}</div>
              </td></tr>
              <tr><td style="font-size:13px;color:#6b7280;line-height:1.6;">
                The code is valid for {{r.ValidMinutes}} minutes and can be used once.
                If you did not mark your attendance just now, tell the office — somebody
                may be using your sign-in.
              </td></tr>
              <tr><td style="padding-top:18px;border-top:1px solid #e3e8ee;font-size:12px;color:#6b7280;">
                {{footer}}<br />
                This message was sent automatically. Please do not reply to it.
              </td></tr>
            </table>
          </td></tr>
        </table>
        """;
    }

    /* ------------------------------------------------- password reset code */

    private static string PasswordResetCodeHtml(PasswordResetCodeMailRequest r) => $$"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6f9;padding:24px 0;">
          <tr><td align="center">
            <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;padding:28px;font-family:Arial,Helvetica,sans-serif;color:#212529;">
              <tr><td style="font-size:12px;letter-spacing:.12em;color:#6b7280;text-transform:uppercase;">Avijan Computer Center</td></tr>
              <tr><td style="padding-top:6px;font-size:18px;font-weight:bold;color:#1b5e8c;">Password reset code</td></tr>
              <tr><td style="padding-top:16px;font-size:15px;">Hello {{r.Name}},</td></tr>
              <tr><td style="padding-top:8px;font-size:15px;line-height:1.6;">
                Use this code to set a new password for your Avijan sign-in.
              </td></tr>
              <tr><td align="center" style="padding:22px 0;">
                <div style="display:inline-block;background:#0a283c;color:#ffffff;font-size:34px;letter-spacing:.28em;font-weight:bold;padding:14px 26px;border-radius:10px;">{{r.Code}}</div>
              </td></tr>
              <tr><td style="font-size:13px;color:#6b7280;line-height:1.6;">
                The code is valid for {{r.ValidMinutes}} minutes and can be used once.
                If you did not ask to reset your password, ignore this message — your
                password has not been changed.
              </td></tr>
              <tr><td style="padding-top:18px;border-top:1px solid #e3e8ee;font-size:12px;color:#6b7280;">
                53, Aghore Sarani, Rajpur, Kol-149 &nbsp;|&nbsp; Mob.: 7688045949<br />
                This message was sent automatically. Please do not reply to it.
              </td></tr>
            </table>
          </td></tr>
        </table>
        """;

    private static string PasswordResetCodeText(PasswordResetCodeMailRequest r) =>
        new StringBuilder()
            .AppendLine("AVIJAN COMPUTER CENTER")
            .AppendLine("Password reset code")
            .AppendLine()
            .AppendLine($"Hello {r.Name},")
            .AppendLine("Use this code to set a new password for your Avijan sign-in.")
            .AppendLine()
            .AppendLine($"    CODE: {r.Code}")
            .AppendLine()
            .AppendLine($"Valid for {r.ValidMinutes} minutes, and can be used once.")
            .AppendLine("If you did not ask to reset your password, ignore this message —")
            .AppendLine("your password has not been changed.")
            .AppendLine()
            .AppendLine("53, Aghore Sarani, Rajpur, Kol-149 | Mob.: 7688045949")
            .AppendLine("This message was sent automatically. Please do not reply to it.")
            .ToString();

    private static string AttendanceCodeText(AttendanceCodeMailRequest r)
    {
        var word = string.Equals(r.Direction, "In", StringComparison.OrdinalIgnoreCase)
            ? "arrival"
            : "departure";

        return new StringBuilder()
            .AppendLine(r.School.ToUpperInvariant())
            .AppendLine("Attendance code")
            .AppendLine()
            .AppendLine($"Hello {r.StaffName},")
            .AppendLine($"Confirm your {word} at {r.MarkedOn:h:mm tt} on {r.MarkedOn:d MMM yyyy}.")
            .AppendLine()
            .AppendLine($"    CODE: {r.Code}")
            .AppendLine()
            .AppendLine($"Valid for {r.ValidMinutes} minutes, and can be used once.")
            .AppendLine("If you did not mark your attendance just now, tell the office.")
            .AppendLine()
            .AppendLine(r.FooterLine)
            .AppendLine("This message was sent automatically. Please do not reply to it.")
            .ToString();
    }
}
