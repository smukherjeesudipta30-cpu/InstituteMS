namespace InstituteMS.Domain.Entities;

/// <summary>
/// A save-and-resume start on the competition portal — maps to
/// <c>dbo.CompetitionDraft</c>.
/// </summary>
/// <remarks>
/// <para>Reserves nothing. Dance capacity is checked and consumed only inside
/// <c>CompetitionService.RegisterAsync</c>'s own transaction, exactly as before this
/// existed — an abandoned draft costs a dance group no seat.</para>
///
/// <para><see cref="RegistrationCode"/> is filled in from the row's own identity
/// right after the insert, not predicted before it — the same reason
/// <c>MonthlyFee.BillNo</c> is derived that way.</para>
/// </remarks>
public class CompetitionDraft
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    public string RegistrationCode { get; set; } = string.Empty;

    public string ParticipantName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Required on every draft started since "forgot password" needed somewhere to
    /// send a reset code — a phone number alone gives no such address. Nullable so
    /// drafts started before that requirement still load; one of those simply
    /// cannot use the emailed-code recovery until it is added by some other route.
    /// </summary>
    public string? Email { get; set; }

    /// <summary><c>Draft</c> or <c>Completed</c>.</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Set once the visitor finishes the full form and it becomes a real registration.</summary>
    public int? LinkedApplicantId { get; set; }

    public DateTime CreatedOn { get; set; }
    public DateTime? LastAccessedOn { get; set; }

    /// <summary>
    /// Hashed the same way as <see cref="PasswordHash"/> — the six-digit code
    /// emailed by "forgot password". Cleared once spent or replaced.
    /// </summary>
    public string? PasswordResetCodeHash { get; set; }
    public DateTime? PasswordResetCodeExpiry { get; set; }
    public int PasswordResetAttempts { get; set; }
}
