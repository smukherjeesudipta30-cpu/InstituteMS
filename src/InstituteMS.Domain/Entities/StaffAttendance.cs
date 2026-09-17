namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.StaffAttendance</c> — one row per QR scan by a member of staff.
/// </summary>
/// <remarks>
/// <para>A scan is an event, not a state: arrival and departure are two rows, and a
/// lunch break is simply two more. Hours are never stored, only derived by pairing
/// each <c>In</c> with the <c>Out</c> that follows it — a stored total goes stale the
/// moment a scan is corrected.</para>
///
/// <para>Absence needs no row. A day with no scan is a day the person did not come
/// in, which is the whole of the rule the owner asked for.</para>
/// </remarks>
public class StaffAttendance
{
    public int Id { get; set; }

    /// <summary><c>Teacher</c> or <c>Receptionist</c>.</summary>
    public string StaffType { get; set; } = string.Empty;

    /// <summary>Row id in <c>dbo.Teacher</c> or <c>dbo.Receptionist</c>.</summary>
    public int StaffId { get; set; }

    /// <summary>
    /// The sign-in that scanned. Kept alongside <see cref="StaffId"/> because a staff
    /// record can be re-pointed at another login later, and an attendance row must
    /// still say who actually stood there.
    /// </summary>
    public string? LoginId { get; set; }

    public string? StaffName { get; set; }

    /// <summary>
    /// The branch whose code was scanned — not the branch on the staff record.
    /// Someone covering a shift elsewhere is present where they are standing.
    /// </summary>
    public int? BranchId { get; set; }

    public string? Branch { get; set; }

    /// <summary>The moment of the scan, to the second. This is the timing that counts.</summary>
    public DateTime ScannedOn { get; set; }

    /// <summary>The calendar day the scan belongs to, so a day groups on an index.</summary>
    public DateOnly OnDate { get; set; }

    /// <summary><c>In</c> or <c>Out</c>, decided by the server from the day's earlier scans.</summary>
    public string Direction { get; set; } = "In";

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>What the phone claimed its own accuracy to be, in metres.</summary>
    public decimal? AccuracyMetres { get; set; }

    /// <summary>How far the phone actually was from the branch, in metres.</summary>
    public decimal? DistanceMetres { get; set; }

    public DateTime CreatedOn { get; set; }
}
