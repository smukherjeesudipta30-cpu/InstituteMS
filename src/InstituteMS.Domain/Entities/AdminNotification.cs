namespace InstituteMS.Domain.Entities;

/// <summary>
/// Something the office should see on the dashboard. Maps to
/// <c>dbo.AdminNotification</c>.
/// </summary>
/// <remarks>
/// This table is the record and the email is a copy of it, not the other way
/// round: an alert that exists only as an email is lost when the mail server is
/// down and unreadable to anyone without that mailbox.
/// </remarks>
public class AdminNotification
{
    public int Id { get; set; }

    /// <summary>
    /// What happened. Use <see cref="AdminNotificationTypes"/> rather than a
    /// literal, so the set of kinds stays discoverable.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>One line, already written for a reader.</summary>
    public string Title { get; set; } = string.Empty;

    public string? StudentName { get; set; }
    public string? CourseName { get; set; }
    public string? RegistrationId { get; set; }
    public string? LoginId { get; set; }

    /// <summary>
    /// The detail the alert exists to carry. For a password change that is the
    /// student's chosen password, in clear — see the migration for what that means.
    /// </summary>
    public string? DetailValue { get; set; }

    /// <summary>Local time: the office reads these against its own clock.</summary>
    public DateTime CreatedOn { get; set; }

    public DateTime? ReadOn { get; set; }
    public string? ReadBy { get; set; }

    public bool IsRead => ReadOn.HasValue;
}

/// <summary>The kinds of notification the dashboard knows how to show.</summary>
public static class AdminNotificationTypes
{
    public const string StudentPasswordChange = "StudentPasswordChange";
}
