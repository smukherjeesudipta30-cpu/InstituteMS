namespace InstituteMS.Domain.Entities;

/// <summary>
/// One year's Drawing &amp; Dance competition — maps to <c>dbo.Competition</c>.
/// </summary>
/// <remarks>
/// A row per year rather than settings in a configuration file, because the owner
/// asked that the amounts, times and rules all be changeable from the admin screen:
/// "Time, Amount, Rules can be changed which will be entered by admin only."
/// </remarks>
public class Competition
{
    public int Id { get; set; }

    public int Year { get; set; }
    public DateOnly? EventDate { get; set; }

    public string? Venue { get; set; }
    public string? VenueBengali { get; set; }

    public decimal DrawingFee { get; set; }
    public decimal DanceFee { get; set; }

    public TimeOnly? DrawingStartTime { get; set; }
    public TimeOnly? DanceStartTime { get; set; }
    public TimeOnly? PrizeGivingTime { get; set; }

    /* The advertisement is shown to the public in both languages, so both are
       stored and both are edited by hand — a machine translation of the rules of a
       competition is not something to put in front of a parent. */
    /* Separate lists per competition. A parent entering only the drawing
       competition should not have to read past the dance rules to find theirs. */
    public string? DrawingRulesEnglish { get; set; }
    public string? DrawingRulesBengali { get; set; }
    public string? DanceRulesEnglish { get; set; }
    public string? DanceRulesBengali { get; set; }
    public string? NoticeEnglish { get; set; }
    public string? NoticeBengali { get; set; }

    /// <summary>
    /// Outside these dates the landing page shows nothing, so last year's event
    /// cannot go on advertising itself.
    /// </summary>
    public DateOnly? RegistrationOpensOn { get; set; }
    public DateOnly? RegistrationClosesOn { get; set; }

    public string? ContactPhones { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public ICollection<CompetitionGroup> Groups { get; set; } = [];
    public ICollection<CompetitionOrganiser> Organisers { get; set; } = [];
}

/// <summary>
/// A drawing division or a dance group — maps to <c>dbo.CompetitionGroup</c>.
/// </summary>
/// <remarks>
/// The two competitions are grouped on different things: drawing by school class
/// (বিভাগ ক to ঘ), dance by age (A, B, C). Both live here because they are the same
/// idea — which bracket an entrant falls into — and keeping them apart would mean
/// two tables that differed only in the column the bracket is read from.
/// </remarks>
public class CompetitionGroup
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    /// <summary><c>Drawing</c> or <c>Dance</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
    public string LabelEnglish { get; set; } = string.Empty;
    public string? LabelBengali { get; set; }

    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public int? MinClass { get; set; }
    public int? MaxClass { get; set; }

    /// <summary>
    /// How many entrants this group takes — 30, 30 and 15 for the three dance
    /// groups. Null for the drawing divisions, which are not capped.
    /// </summary>
    /// <remarks>
    /// This replaced the admin-set batch collection time. The owner's reasoning was
    /// that a printed chest number does the job on its own: "candidate able to
    /// download Batch like 1,2 etc, which will taken printout and kept in chest at
    /// the time of dance only then no batch collection timing will not be required
    /// from admin it self".
    /// </remarks>
    public int? Capacity { get; set; }

    public int SortOrder { get; set; }

    public Competition? Competition { get; set; }
}

/// <summary>
/// A name shown on the registration form — maps to <c>dbo.CompetitionOrganiser</c>.
/// </summary>
/// <remarks>
/// Anindita, Nargis and Arkita to begin with, and a table rather than three names in
/// the markup because the owner asked that "later new name can be added and this
/// name can be modified also".
/// </remarks>
public class CompetitionOrganiser
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Competition? Competition { get; set; }
}

/// <summary>
/// One submitted registration form — maps to <c>dbo.CompetitionApplicant</c>.
/// </summary>
/// <remarks>
/// <para>Deliberately unrelated to <c>dbo.student</c> and <c>dbo.Users</c>. These are
/// children from any school, entering once; the owner settled it plainly — "This will
/// be for anonymus - no relationship with existing student or user".</para>
///
/// <para>Separate from <see cref="CompetitionEntry"/> because one form and one
/// payment may enter both competitions, which are judged separately and each need
/// their own number.</para>
/// </remarks>
public class CompetitionApplicant
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    public string ParticipantName { get; set; } = string.Empty;
    public string? GuardianName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? SchoolName { get; set; }
    public string? ClassName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary><c>Online</c>, or <c>Venue</c> for drawing paid on arrival.</summary>
    public string PaymentMode { get; set; } = "Online";

    public decimal Amount { get; set; }

    /// <summary>
    /// The UPI reference the payer typed in.
    /// </summary>
    /// <remarks>
    /// Nothing in this application can ask the bank whether that payment happened —
    /// the QR is static and no gateway reports back. A unique index makes the one
    /// check that is possible: the same reference cannot be used by two entrants.
    /// Confirming it is an administrator's act, from the dashboard, exactly as the
    /// students' own online fee payments are confirmed today.
    /// </remarks>
    public string? TransactionRef { get; set; }

    /// <summary><c>Pending</c>, <c>Confirmed</c>, <c>AtVenue</c> or <c>Rejected</c>.</summary>
    public string PaymentStatus { get; set; } = "Pending";

    /// <summary>Why the office cancelled it. Required when cancelling.</summary>
    public string? CancelReason { get; set; }

    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedOn { get; set; }

    public DateTime SubmittedOn { get; set; }
    public string? SourceIp { get; set; }

    public Competition? Competition { get; set; }
    public ICollection<CompetitionEntry> Entries { get; set; } = [];
    public ICollection<CompetitionDocument> Documents { get; set; } = [];
}

/// <summary>
/// One competition an applicant entered — maps to <c>dbo.CompetitionEntry</c>.
/// </summary>
public class CompetitionEntry
{
    public int Id { get; set; }
    public int ApplicantId { get; set; }
    public int CompetitionId { get; set; }

    /// <summary><c>Drawing</c> or <c>Dance</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public int? GroupId { get; set; }

    /// <summary>
    /// The number within this competition and year. Kept beside the formatted
    /// <see cref="RegistrationNo"/> so the next one is a MAX over an integer rather
    /// than arithmetic on a string.
    /// </summary>
    public int Serial { get; set; }

    /// <summary>What the entrant brings on the day — <c>AVI/DRW/2027/001</c>.</summary>
    public string RegistrationNo { get; set; } = string.Empty;

    /// <summary>
    /// The chest number a dancer prints and pins on -- 1 upwards within their own
    /// group. Null for drawing, which has no such thing.
    /// </summary>
    public int? BatchNo { get; set; }

    public DateTime CreatedOn { get; set; }

    public CompetitionApplicant? Applicant { get; set; }
    public CompetitionGroup? Group { get; set; }
}

/// <summary>
/// A photograph or a paper attached to a registration — maps to
/// <c>dbo.CompetitionDocument</c>.
/// </summary>
/// <remarks>
/// Held inline as Base64, the way student and staff photographs already are, so a
/// document travels with the database backup rather than living on a file share that
/// has to be backed up and restored in step.
/// </remarks>
public class CompetitionDocument
{
    public int Id { get; set; }
    public int ApplicantId { get; set; }

    /// <summary><c>Photo</c>, <c>SchoolId</c>, <c>BirthCertificate</c> or <c>PaymentSlip</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }

    public CompetitionApplicant? Applicant { get; set; }
}

/// <summary>
/// One of the printed posters, shown on the registration portal — maps to
/// <c>dbo.CompetitionAdvert</c>.
/// </summary>
/// <remarks>
/// Held per competition year rather than as a file in the site, because the owner
/// asked to put last year's artwork up for testing and replace it next year from the
/// same screen: "right now for testing old year advertisement can up from next year
/// new advertisement can be up".
/// </remarks>
public class CompetitionAdvert
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    /// <summary><c>Drawing</c> or <c>Dance</c>.</summary>
    public string Slot { get; set; } = string.Empty;

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>So the portal can reserve the right shape before the image loads.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    public string? UploadedBy { get; set; }
    public DateTime UploadedOn { get; set; }

    public Competition? Competition { get; set; }
}
