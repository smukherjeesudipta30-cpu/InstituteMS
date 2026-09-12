using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Org;

/* ------------------------------------------------------------ the chart */

/// <summary>One box on the chart, as everybody sees it.</summary>
public record OrgNodeDto(
    int Id,
    string PersonName,

    /// <summary>The position the administrator typed — "Owner", "Head Receptionist &amp; HR".</summary>
    string? Designation,

    string? Qualification,

    /// <summary>What computer training this person holds. Blank for technical staff.</summary>
    string? ComputerTraining,

    /// <summary>Technical staff, excused the qualification and computer-training requirement.</summary>
    bool IsTechnical,

    /// <summary>
    /// Every branch this person belongs to. Empty means the whole centre -- the
    /// owner, or a function such as Technical Support.
    /// </summary>
    IReadOnlyList<string> Branches,

    bool IsActive,
    int SortOrder,

    string? StaffType,
    int? StaffId,

    /// <summary>True when a photograph has been uploaded; fetch it from the photo endpoint.</summary>
    bool HasPhoto,

    /// <summary>
    /// True when this box was not created by anybody — it is a teacher or
    /// receptionist drawn beneath the default parent because they are on the staff
    /// register. It has no row behind it, so it cannot be edited or deleted from the
    /// chart; its <see cref="Id"/> is negative and exists only to hang the drawing
    /// on. Change these people from the staff master, or give one a box of their own
    /// to override what is shown.
    /// </summary>
    bool IsAutomatic,

    /// <summary>
    /// True on the one box that teachers and receptionists hang beneath. Stated
    /// rather than left to be inferred from where the automatic boxes point: once
    /// every member of staff has a box of their own there are no automatic boxes to
    /// infer it from, and the editor would show the rule as switched off.
    /// </summary>
    bool IsDefaultStaffParent,

    /// <summary>
    /// Every box directly above this one. More than one is normal — Technical
    /// Support answers to two people.
    /// </summary>
    IReadOnlyList<int> ParentIds,

    /// <summary>
    /// Only present for the administrator. Students and staff see the chart without
    /// anybody's email address on it.
    /// </summary>
    string? Email);

public record OrgChartDto(
    IReadOnlyList<OrgNodeDto> Nodes,

    /// <summary>Boxes with nobody above them. Usually one, but the chart does not insist.</summary>
    IReadOnlyList<int> RootIds,

    DateTime GeneratedOn);

/* ------------------------------------------------------------- editing */

public record OrgNodeSaveRequest
{
    public string PersonName { get; init; } = string.Empty;
    public string? Designation { get; init; }
    public string? Qualification { get; init; }

    /// <summary>Required unless <see cref="IsTechnical"/>.</summary>
    public string? ComputerTraining { get; init; }

    /// <summary>Technical staff are excused qualification and computer training.</summary>
    public bool IsTechnical { get; init; }
    public string? Email { get; init; }
    /// <summary>Every branch this person belongs to. Send none for the whole centre.</summary>
    public IReadOnlyList<string> Branches { get; init; } = [];
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }

    /// <summary>Optional link to a staff record. Null for a function such as Technical Support.</summary>
    public string? StaffType { get; init; }
    public int? StaffId { get; init; }

    /// <summary>
    /// Tick this on the one box that teachers and receptionists should hang beneath.
    /// Ticking it here unticks it wherever it was before, so the chart never draws
    /// the same person twice.
    /// </summary>
    public bool IsDefaultStaffParent { get; init; }

    /// <summary>
    /// Every box this one reports to. Sent whole rather than added one at a time, so
    /// what is stored is exactly what the administrator was looking at.
    /// </summary>
    public IReadOnlyList<int> ParentIds { get; init; } = [];
}

public class OrgNodeSaveRequestValidator : AbstractValidator<OrgNodeSaveRequest>
{
    public OrgNodeSaveRequestValidator()
    {
        RuleFor(x => x.PersonName)
            .NotEmpty().WithMessage("Enter the name for this box.")
            .MaximumLength(150);

        RuleFor(x => x.Designation).MaximumLength(150);
        RuleFor(x => x.Qualification).MaximumLength(200);
        RuleFor(x => x.ComputerTraining).MaximumLength(200);

        /* Required of everyone on the chart except technical staff, matching the
           teacher and receptionist masters. A box that stands for a function rather
           than a person -- Technical Support -- is exactly what the tick is for. */
        RuleFor(x => x.Qualification)
            .NotEmpty().WithMessage("Enter this person's qualification.")
            .When(x => !x.IsTechnical);

        RuleFor(x => x.ComputerTraining)
            .NotEmpty().WithMessage("Enter this person's computer training.")
            .When(x => !x.IsTechnical);
        RuleForEach(x => x.Branches)
            .NotEmpty().WithMessage("A blank branch name cannot be saved.")
            .MaximumLength(100);

        RuleFor(x => x.Branches)
            .Must(b => b.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == b.Count)
            .WithMessage("That branch is listed twice.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.StaffType)
            .Must(t => t is null or "Teacher" or "Receptionist")
            .WithMessage("Staff type must be Teacher or Receptionist.");
    }
}

/// <summary>A photograph for one box.</summary>
public record OrgPhotoDto(byte[] Content, string ContentType, string FileName);

/* ------------------------------------------------------------ contract */

public interface IOrgService
{
    /// <summary>
    /// The whole chart. <paramref name="includeEmail"/> is true only for the back
    /// office — the chart is shown to students, and their teachers' addresses are
    /// not theirs to have.
    /// </summary>
    Task<OrgChartDto> GetChartAsync(bool includeEmail, CancellationToken ct = default);

    /// <summary>
    /// One student's own line of the hierarchy: the owner at the top, and beneath
    /// them the teacher or teachers that student is actually assigned to.
    /// </summary>
    /// <remarks>
    /// <para>Built from the student's record rather than from anything typed twice,
    /// so it is right the moment an admission is taken and it follows a teacher
    /// change without anybody revisiting the chart.</para>
    ///
    /// <para>Never carries an email address: this is shown to the student.</para>
    /// </remarks>
    Task<Result<OrgChartDto>> GetStudentChartAsync(
        string registrationId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(OrgNodeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, OrgNodeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);

    Task<Result<bool>> SetPhotoAsync(
        int id, byte[] content, string fileName, string contentType, string? actor, CancellationToken ct = default);

    Task<Result<OrgPhotoDto>> GetPhotoAsync(int id, CancellationToken ct = default);
}
