namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.Student</c>. C# names are readable; the column names stay
/// exactly as the legacy schema has them (configured in StudentConfiguration).
/// </summary>
public class Student
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? GuardianName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Business key, format <c>AVI/{n}/{yy}</c>.</summary>
    public string? RegistrationId { get; set; }
    /// <summary>Admission bill number, format <c>S-A-{n}-{yy}</c>.</summary>
    public string? BillNo { get; set; }
    public int? TechniqueCount { get; set; }
    public decimal? AdmissionFees { get; set; }

    /// <summary>
    /// <c>Cash</c> or <c>Bank</c> — how the admission fee was received. Same
    /// distinction as <c>MonthlyFee.PaymentMode</c>, added for the same reason: a
    /// month-end reconciliation by receptionist and branch needs to know how each
    /// payment arrived, not only how much it was. Null on students admitted before
    /// migration 044.
    /// </summary>
    public string? AdmissionPaymentMode { get; set; }

    /// <summary>
    /// How much of <see cref="AdmissionFees"/> has actually been received, across
    /// the payment taken at admission and any later instalment. The balance owed is
    /// <c>AdmissionFees - AdmissionFeesPaid</c>; <see cref="AdmissionFeePayment"/>
    /// carries the receipt behind each part of it.
    /// </summary>
    /// <remarks>
    /// Backfilled equal to <see cref="AdmissionFees"/> for every student admitted
    /// before migration 051. Until then the admission fee was all-or-nothing — the
    /// amount entered was the amount taken — so treating those as paid in full is
    /// what the records actually mean. Leaving them null and calling the difference
    /// a due would invent an unpaid balance for all 957 of them.
    /// </remarks>
    public decimal? AdmissionFeesPaid { get; set; }

    /// <summary>
    /// Who referred this student: "Teacher" or "Receptionist", with <see cref="ReferrerId"/>.
    /// Required for new admissions; null for students admitted before referrals existed.
    /// </summary>
    public string? ReferrerType { get; set; }
    public int? ReferrerId { get; set; }

    /// <summary>The referral rate locked in at admission from the slab table.</summary>
    public decimal? ReferralPercent { get; set; }

    public decimal? MonthlyFees { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfExit { get; set; }
    public int? CourseId { get; set; }
    public int? PrimaryTeacherId { get; set; }
    public int? SecondTeacherId { get; set; }
    public int? ThirdTeacherId { get; set; }
    /// <summary>
    /// Nullable because the legacy columns are. Both are <c>bit NULL</c> in
    /// <c>dbo.student</c> and six production students have <c>IsComplete = NULL</c>;
    /// a non-nullable property makes EF read the column with GetBoolean and throw
    /// "Data is Null" on those rows before any converter can intervene.
    ///
    /// Treat NULL as false at every use: an unknown completion is not a
    /// completion, so no certificate is offered on the strength of a missing value.
    /// </summary>
    public bool? IsActive { get; set; }

    /// <inheritdoc cref="IsActive"/>
    public bool? IsComplete { get; set; }
    /// <summary>User who created/last touched the row (legacy <c>patCode</c> = Session["UserID"]).</summary>
    public string? LoginId { get; set; }
    public string? Branch { get; set; }
    public string? PersonalEmail { get; set; }
    public string? AvijanLoginEmail { get; set; }

    /// <summary>
    /// The student portal password, generated when the student is registered.
    /// <b>Stored in clear text</b>, exactly as production does - the office needs to
    /// be able to read it back to the student. The account they actually sign in
    /// with is <c>dbo.Users</c>, where the same value is held as a BCrypt hash.
    /// </summary>
    public string? AvijanLoginPassword { get; set; }
    /// <summary>Legacy self-reference written by <c>usp_StudentSaveData</c> (Replica_id = own id).</summary>
    public int? ReplicaId { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public Course? Course { get; set; }
    public Teacher? PrimaryTeacher { get; set; }
    public Teacher? SecondTeacher { get; set; }
    public Teacher? ThirdTeacher { get; set; }
}
