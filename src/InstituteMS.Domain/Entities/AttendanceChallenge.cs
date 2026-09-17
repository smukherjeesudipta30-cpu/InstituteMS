namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.AttendanceChallenge</c> — an attendance mark waiting for the
/// six-digit code emailed to the staff member.
/// </summary>
/// <remarks>
/// <para><see cref="MarkedOn"/> is when the person pressed the button, and it is what
/// becomes the attendance time once the code is accepted. Taking the time at
/// confirmation instead would let a slow email shorten somebody's working day.</para>
///
/// <para>The location is captured here too, so the fence is judged where they stood
/// when they marked — not where they happen to be when the code arrives.</para>
/// </remarks>
public class AttendanceChallenge
{
    public int Id { get; set; }

    public string StaffType { get; set; } = string.Empty;
    public int StaffId { get; set; }
    public string? StaffName { get; set; }
    public string? LoginId { get; set; }

    public int? BranchId { get; set; }
    public string? Branch { get; set; }

    /// <summary>When the button was pressed. Becomes the attendance time.</summary>
    public DateTime MarkedOn { get; set; }

    public DateOnly OnDate { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AccuracyMetres { get; set; }
    public decimal? DistanceMetres { get; set; }

    /// <summary>BCrypt of the six digits — never the digits themselves.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public string? SentToEmail { get; set; }

    public DateTime ExpiresOn { get; set; }

    /// <summary>Wrong guesses so far. The challenge dies after a handful.</summary>
    public int Attempts { get; set; }

    /// <summary>Set when accepted; a consumed challenge cannot be reused.</summary>
    public DateTime? ConsumedOn { get; set; }

    public DateTime CreatedOn { get; set; }
}
