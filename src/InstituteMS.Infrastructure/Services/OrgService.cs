using InstituteMS.Application.Common;
using InstituteMS.Application.Org;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The staff hierarchy: the boxes, the lines between them, and the rules that keep
/// the chart a chart.
/// </summary>
public class OrgService : IOrgService
{
    /// <summary>A photograph larger than this is a camera file nobody resized.</summary>
    private const int MaxPhotoBytes = 3 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly ILogger<OrgService> _logger;

    public OrgService(AppDbContext db, ILogger<OrgService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /* ============================================================= reading */

    public async Task<OrgChartDto> GetChartAsync(bool includeEmail, CancellationToken ct = default)
    {
        var nodes = await _db.OrgNodes
            .AsNoTracking()
            .OrderBy(n => n.SortOrder).ThenBy(n => n.PersonName)
            .Select(n => new
            {
                n.Id, n.PersonName, n.Designation, n.Qualification,
                n.ComputerTraining, n.IsTechnical, n.Branch,
                n.IsActive, n.SortOrder, n.StaffType, n.StaffId, n.Email,
                n.IsDefaultStaffParent,
                HasPhoto = n.PhotoBase64 != null && n.PhotoBase64 != ""
            })
            .ToListAsync(ct);

        var edges = await _db.OrgEdges
            .AsNoTracking()
            .Select(e => new { e.ParentId, e.ChildId })
            .ToListAsync(ct);

        var parentsOf = edges
            .GroupBy(e => e.ChildId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(e => e.ParentId).ToList());

        var dtos = nodes.Select(n => new OrgNodeDto(
            n.Id, n.PersonName, n.Designation, n.Qualification,
            n.ComputerTraining, n.IsTechnical, SplitBranches(n.Branch),
            n.IsActive, n.SortOrder, n.StaffType, n.StaffId, n.HasPhoto,
            IsAutomatic: false, n.IsDefaultStaffParent,
            parentsOf.TryGetValue(n.Id, out var parents) ? parents : [],
            // Withheld unless the caller is back office — see the interface.
            includeEmail ? n.Email : null)).ToList();

        dtos.AddRange(await AutomaticStaffAsync(nodes.Select(n => new StaffLink(
            n.Id, n.StaffType, n.StaffId, n.IsDefaultStaffParent, n.PersonName)), includeEmail, ct));

        var roots = dtos.Where(n => n.ParentIds.Count == 0).Select(n => n.Id).ToList();

        return new OrgChartDto(dtos, roots, DateTime.Now);
    }

    /// <summary>
    /// One student's own line of the hierarchy: the owner, then their own teachers.
    /// </summary>
    /// <remarks>
    /// <para>The general chart answers "who runs the centre". This answers "who
    /// teaches me", which is the only part of it a student is really asking about,
    /// and it comes straight off their student record — so it is correct from the
    /// moment the admission is taken, and it follows a change of teacher on its own.
    /// Nothing here is entered a second time.</para>
    ///
    /// <para>A teacher who has a box on the chart is shown with everything that box
    /// carries — photograph, position, qualification. A teacher who has none is
    /// drawn from their staff record instead, so a student is never told their
    /// teacher does not exist merely because nobody has built a box for them.</para>
    /// </remarks>
    public async Task<Result<OrgChartDto>> GetStudentChartAsync(
        string registrationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            return Result<OrgChartDto>.Failure("No student to build the chart for.");

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == registrationId)
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.PrimaryTeacherId, s.SecondTeacherId, s.ThirdTeacherId, s.Branch
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<OrgChartDto>.Failure("That student record no longer exists.", ResultStatus.NotFound);

        /* The owner is whoever sits at the top of the real chart, not a name written
           in here — if the top of the centre ever changes, this follows. */
        var owner = await _db.OrgNodes
            .AsNoTracking()
            .Where(n => n.IsActive && !_db.OrgEdges.Any(e => e.ChildId == n.Id))
            .OrderBy(n => n.SortOrder).ThenBy(n => n.Id)
            .Select(n => new
            {
                n.Id, n.PersonName, n.Designation, n.Qualification, n.ComputerTraining,
                n.IsTechnical, n.Branch,
                HasPhoto = n.PhotoBase64 != null && n.PhotoBase64 != ""
            })
            .FirstOrDefaultAsync(ct);

        var nodes = new List<OrgNodeDto>();
        var ownerId = owner?.Id ?? 0;

        if (owner is not null)
        {
            nodes.Add(new OrgNodeDto(
                owner.Id, owner.PersonName, owner.Designation, owner.Qualification,
                owner.ComputerTraining, owner.IsTechnical, SplitBranches(owner.Branch),
                IsActive: true, SortOrder: 0, StaffType: null, StaffId: null,
                HasPhoto: owner.HasPhoto, IsAutomatic: false, IsDefaultStaffParent: false,
                ParentIds: [], Email: null));
        }

        var teacherIds = new[] { student.PrimaryTeacherId, student.SecondTeacherId, student.ThirdTeacherId }
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (teacherIds.Count > 0)
        {
            // A box of their own wins, because it carries the photograph and the
            // position somebody chose to show.
            var boxes = await _db.OrgNodes
                .AsNoTracking()
                .Where(n => n.StaffType == "Teacher" && n.StaffId != null
                            && teacherIds.Contains(n.StaffId.Value))
                .Select(n => new
                {
                    n.Id, n.PersonName, n.Designation, n.Qualification, n.ComputerTraining,
                    n.IsTechnical, n.Branch, n.StaffId, n.SortOrder,
                    HasPhoto = n.PhotoBase64 != null && n.PhotoBase64 != ""
                })
                .ToListAsync(ct);

            var records = await _db.Teachers
                .AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .Select(t => new
                {
                    t.Id, t.Name, t.Designation, t.Qualification, t.ComputerTraining,
                    t.IsTechnical, t.Branch
                })
                .ToListAsync(ct);

            var parentIds = ownerId > 0 ? new[] { ownerId } : [];

            foreach (var teacherId in teacherIds)
            {
                var box = boxes.FirstOrDefault(b => b.StaffId == teacherId);

                if (box is not null)
                {
                    nodes.Add(new OrgNodeDto(
                        box.Id, box.PersonName, box.Designation, box.Qualification,
                        box.ComputerTraining, box.IsTechnical, SplitBranches(box.Branch),
                        IsActive: true, SortOrder: box.SortOrder, StaffType: "Teacher",
                        StaffId: teacherId, HasPhoto: box.HasPhoto, IsAutomatic: false,
                        IsDefaultStaffParent: false, ParentIds: parentIds, Email: null));

                    continue;
                }

                var record = records.FirstOrDefault(t => t.Id == teacherId);
                if (record is null) continue;

                nodes.Add(new OrgNodeDto(
                    AutomaticId("Teacher", teacherId),
                    string.IsNullOrWhiteSpace(record.Name) ? "(unnamed)" : record.Name!,
                    record.Designation, record.Qualification, record.ComputerTraining,
                    record.IsTechnical, SplitBranches(record.Branch),
                    IsActive: true, SortOrder: 0, StaffType: "Teacher", StaffId: teacherId,
                    HasPhoto: false, IsAutomatic: true, IsDefaultStaffParent: false,
                    ParentIds: parentIds, Email: null));
            }
        }

        var roots = nodes.Where(n => n.ParentIds.Count == 0).Select(n => n.Id).ToList();

        return Result<OrgChartDto>.Success(new OrgChartDto(nodes, roots, DateTime.Now));
    }

    private readonly record struct StaffLink(
        int NodeId, string? StaffType, int? StaffId, bool IsDefaultStaffParent, string? PersonName);

    /// <summary>
    /// Names compared the way a person would read them: case and stray spacing are
    /// not what makes two boxes different people.
    /// </summary>
    private static string NormaliseName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToLowerInvariant();

    /// <summary>
    /// Every teacher and receptionist who has no box of their own, drawn beneath the
    /// box marked as the default parent for staff.
    /// </summary>
    /// <remarks>
    /// <para>The owner was explicit that the chart is "dynamic only", with "no fixed
    /// count of teacher, receptionist, Technical staff and all", and that a teacher
    /// added later must appear. So the chart holds no roster of staff: it asks the
    /// staff register who is on it, every time it is drawn. Somebody hired this
    /// morning is on the chart this morning; somebody marked inactive drops off it
    /// without anybody editing the hierarchy.</para>
    ///
    /// <para>These boxes have no row behind them. Their ids are negative and derived
    /// from the staff id, so they are stable between requests (the browser can keep a
    /// box expanded across a refresh) and can never collide with a real node id. Give
    /// someone a real box linked to their staff record and the automatic one steps
    /// aside, which is how a photograph or a different name gets onto the chart.</para>
    ///
    /// <para>If no box is marked as the default parent, nothing is added — an
    /// administrator who has not yet said where staff belong sees only what they
    /// built, rather than thirty-odd boxes floating with no line to anyone.</para>
    /// </remarks>
    private async Task<List<OrgNodeDto>> AutomaticStaffAsync(
        IEnumerable<StaffLink> existing, bool includeEmail, CancellationToken ct)
    {
        var links = existing.ToList();
        var parent = links.FirstOrDefault(n => n.IsDefaultStaffParent);

        if (parent.NodeId == 0) return [];

        var parentIds = new[] { parent.NodeId };

        var spokenFor = links
            .Where(n => n.StaffType is not null && n.StaffId is > 0)
            .Select(n => (Type: n.StaffType!, Id: n.StaffId!.Value))
            .ToHashSet();

        /* The same test by name, for boxes made by hand without the staff picker.
           Linking is the intended way to say "this box is that person", but a box
           typed in before the picker existed — or with it left alone — carries no
           StaffId at all, so the check above cannot see it, and the person is drawn
           twice: once by hand, once automatically. That is the duplicate the office
           keeps reporting.

           A hand-made box wins: it is the one somebody deliberately positioned and
           may have given a photograph to, and the automatic one is meant to step
           aside for it. Two genuinely different people sharing a name would collapse
           into one box here, which is the accepted cost — it is rarer than the
           duplicate, and visible (someone is missing) rather than silent. Linking the
           box to its staff record remains the exact answer; this only stops an
           unlinked one from duplicating in the meantime. */
        var namesSpokenFor = links
            .Select(n => NormaliseName(n.PersonName))
            .Where(n => n.Length > 0)
            .ToHashSet();

        var result = new List<OrgNodeDto>();

        var teachers = await _db.Teachers
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.Name, t.Designation, t.Qualification, t.ComputerTraining,
                t.IsTechnical, t.Branch, t.PersonalEmail
            })
            .ToListAsync(ct);

        foreach (var t in teachers)
        {
            if (spokenFor.Contains(("Teacher", t.Id))) continue;
            if (namesSpokenFor.Contains(NormaliseName(t.Name))) continue;

            // Shown only once the administrator has actually typed a position for
            // this person. The register has 32-odd teachers, several of them stray
            // duplicates or blank imports from the old system, and drawing every
            // one of them by default turned the chart into noise the owner had to
            // read past — "Whichever not entered by admin yet, that will not show
            // now." Filling in Position is what puts someone on the chart.
            if (string.IsNullOrWhiteSpace(t.Designation)) continue;

            result.Add(new OrgNodeDto(
                AutomaticId("Teacher", t.Id),
                string.IsNullOrWhiteSpace(t.Name) ? "(unnamed)" : t.Name!,
                t.Designation, t.Qualification, t.ComputerTraining, t.IsTechnical,
                SplitBranches(t.Branch),
                IsActive: true, SortOrder: 0, StaffType: "Teacher", StaffId: t.Id,
                HasPhoto: false, IsAutomatic: true, IsDefaultStaffParent: false,
                ParentIds: parentIds,
                Email: includeEmail ? t.PersonalEmail : null));
        }

        var receptionists = await _db.Receptionists
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.Designation, r.Qualification, r.ComputerTraining,
                r.IsTechnical, r.Branch, r.PersonalEmail
            })
            .ToListAsync(ct);

        foreach (var r in receptionists)
        {
            if (spokenFor.Contains(("Receptionist", r.Id))) continue;
            if (namesSpokenFor.Contains(NormaliseName(r.Name))) continue;
            if (string.IsNullOrWhiteSpace(r.Designation)) continue;

            result.Add(new OrgNodeDto(
                AutomaticId("Receptionist", r.Id),
                string.IsNullOrWhiteSpace(r.Name) ? "(unnamed)" : r.Name,
                r.Designation, r.Qualification, r.ComputerTraining, r.IsTechnical,
                SplitBranches(r.Branch),
                IsActive: true, SortOrder: 0, StaffType: "Receptionist", StaffId: r.Id,
                HasPhoto: false, IsAutomatic: true, IsDefaultStaffParent: false,
                ParentIds: parentIds,
                Email: includeEmail ? r.PersonalEmail : null));
        }

        return result;
    }

    /// <summary>
    /// A negative id for a box that has no row: teachers on the even numbers,
    /// receptionists on the odd, so the two registers cannot land on the same id.
    /// </summary>
    private static int AutomaticId(string staffType, int staffId) =>
        staffType == "Teacher" ? -(staffId * 2) : -(staffId * 2 + 1);

    /* ============================================================= editing */

    public async Task<Result<int>> CreateAsync(
        OrgNodeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var node = new OrgNode
        {
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        Apply(request, node);

        if (request.IsDefaultStaffParent) await ClearOtherDefaultParentsAsync(0, ct);

        _db.OrgNodes.Add(node);
        await _db.SaveChangesAsync(ct);

        var parents = await SetParentsAsync(node.Id, request.ParentIds, actor, ct);

        if (!parents.Succeeded)
        {
            // The box exists but the lines were refused; say so rather than leaving
            // the administrator thinking the whole thing failed.
            return Result<int>.Failure(parents.Error!, parents.Status);
        }

        return Result<int>.Success(node.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, OrgNodeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var node = await _db.OrgNodes.FirstOrDefaultAsync(n => n.Id == id, ct);

        if (node is null)
            return Result<bool>.Failure("That box is no longer on the chart.", ResultStatus.NotFound);

        if (request.IsDefaultStaffParent) await ClearOtherDefaultParentsAsync(id, ct);

        Apply(request, node);
        node.ModifiedBy = actor;
        node.ModifiedOn = DateTime.Now;

        var parents = await SetParentsAsync(id, request.ParentIds, actor, ct);

        if (!parents.Succeeded)
            return parents;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var node = await _db.OrgNodes.FirstOrDefaultAsync(n => n.Id == id, ct);

        if (node is null)
            return Result<bool>.Failure("That box is no longer on the chart.", ResultStatus.NotFound);

        /* Removing a box with people under it would orphan them — they would vanish
           from the chart without anybody deciding they should. Move them first. */
        var below = await _db.OrgEdges.CountAsync(e => e.ParentId == id, ct);

        if (below > 0)
        {
            return Result<bool>.Failure(
                $"{below} box(es) report to {node.PersonName}. Move them under someone else first, " +
                "then remove this one.",
                ResultStatus.Conflict);
        }

        var lines = await _db.OrgEdges.Where(e => e.ChildId == id).ToListAsync(ct);
        _db.OrgEdges.RemoveRange(lines);
        _db.OrgNodes.Remove(node);

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /* ============================================================== photo */

    public async Task<Result<bool>> SetPhotoAsync(
        int id, byte[] content, string fileName, string contentType, string? actor,
        CancellationToken ct = default)
    {
        var node = await _db.OrgNodes.FirstOrDefaultAsync(n => n.Id == id, ct);

        if (node is null)
            return Result<bool>.Failure("That box is no longer on the chart.", ResultStatus.NotFound);

        if (content.Length == 0)
            return Result<bool>.Failure("That file is empty.");

        if (content.Length > MaxPhotoBytes)
        {
            return Result<bool>.Failure(
                $"That photograph is {content.Length / 1024 / 1024} MB. Please use one under 3 MB.");
        }

        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Failure("That file is not an image.");

        node.PhotoBase64 = Convert.ToBase64String(content);
        node.PhotoContentType = contentType;
        node.PhotoFileName = fileName;
        node.ModifiedBy = actor;
        node.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<OrgPhotoDto>> GetPhotoAsync(int id, CancellationToken ct = default)
    {
        var node = await _db.OrgNodes
            .AsNoTracking()
            .Where(n => n.Id == id)
            .Select(n => new { n.PhotoBase64, n.PhotoContentType, n.PhotoFileName })
            .FirstOrDefaultAsync(ct);

        if (node?.PhotoBase64 is null or "")
            return Result<OrgPhotoDto>.Failure("No photograph for that box.", ResultStatus.NotFound);

        try
        {
            return Result<OrgPhotoDto>.Success(new OrgPhotoDto(
                Convert.FromBase64String(node.PhotoBase64),
                node.PhotoContentType ?? "image/jpeg",
                node.PhotoFileName ?? $"org-{id}.jpg"));
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Org photo for node {Id} is not valid Base64.", id);
            return Result<OrgPhotoDto>.Failure("That photograph could not be read.", ResultStatus.NotFound);
        }
    }

    /* ============================================================ helpers */

    /// <summary>
    /// Moves the "staff hang here" mark onto one box, taking it off whoever had it.
    /// </summary>
    /// <remarks>
    /// The database allows only one, so clearing the old one first is what makes the
    /// move possible at all rather than a unique-index error the administrator would
    /// have to decipher.
    /// </remarks>
    private async Task ClearOtherDefaultParentsAsync(int keepId, CancellationToken ct)
    {
        var others = await _db.OrgNodes
            .Where(n => n.IsDefaultStaffParent && n.Id != keepId)
            .ToListAsync(ct);

        foreach (var other in others) other.IsDefaultStaffParent = false;

        if (others.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private static void Apply(OrgNodeSaveRequest request, OrgNode node)
    {
        node.PersonName = request.PersonName.Trim();
        node.Designation = Blank(request.Designation);
        node.Qualification = Blank(request.Qualification);
        node.ComputerTraining = Blank(request.ComputerTraining);
        node.IsTechnical = request.IsTechnical;
        node.Email = Blank(request.Email);
        node.Branch = JoinBranches(request.Branches);
        node.IsActive = request.IsActive;
        node.SortOrder = request.SortOrder;
        node.StaffType = Blank(request.StaffType);
        node.StaffId = request.StaffId is > 0 ? request.StaffId : null;
        node.IsDefaultStaffParent = request.IsDefaultStaffParent;
    }

    /// <summary>
    /// The stored list read back out. Split rather than joined to a child table
    /// because branch here is a label on a drawing -- see migration 037.
    /// </summary>
    private static IReadOnlyList<string> SplitBranches(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? JoinBranches(IReadOnlyList<string> branches)
    {
        var names = branches
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Replaces every line above one box.
    /// </summary>
    /// <remarks>
    /// Sent whole rather than added one at a time, so what ends up stored is exactly
    /// what the administrator was looking at when they pressed save — no half-applied
    /// edit, and no line surviving because its removal was missed.
    /// </remarks>
    private async Task<Result<bool>> SetParentsAsync(
        int childId, IReadOnlyList<int> parentIds, string? actor, CancellationToken ct)
    {
        var wanted = parentIds.Where(p => p > 0).Distinct().ToList();

        if (wanted.Contains(childId))
            return Result<bool>.Failure("A box cannot report to itself.");

        if (wanted.Count > 0)
        {
            var known = await _db.OrgNodes.CountAsync(n => wanted.Contains(n.Id), ct);

            if (known != wanted.Count)
                return Result<bool>.Failure("One of those boxes is no longer on the chart.");

            /* A loop — A under B under A — would make the chart impossible to draw
               and every walk of it non-terminating. The database can refuse a box
               reporting to itself with a CHECK; a longer ring has to be found here. */
            var edges = await _db.OrgEdges
                .AsNoTracking()
                .Where(e => e.ChildId != childId)
                .Select(e => new { e.ParentId, e.ChildId })
                .ToListAsync(ct);

            var parentsOf = edges
                .GroupBy(e => e.ChildId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ParentId).ToList());

            foreach (var candidate in wanted)
            {
                if (ReachesUpTo(candidate, childId, parentsOf))
                {
                    var name = await _db.OrgNodes
                        .Where(n => n.Id == candidate)
                        .Select(n => n.PersonName)
                        .FirstOrDefaultAsync(ct);

                    return Result<bool>.Failure(
                        $"{name} already reports to this box, directly or through someone else. " +
                        "Putting them above it as well would make a loop.");
                }
            }
        }

        var current = await _db.OrgEdges.Where(e => e.ChildId == childId).ToListAsync(ct);
        _db.OrgEdges.RemoveRange(current);

        foreach (var parentId in wanted)
        {
            _db.OrgEdges.Add(new OrgEdge
            {
                ParentId = parentId,
                ChildId = childId,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// True when <paramref name="target"/> sits somewhere above <paramref name="from"/>.
    /// Walks upwards, remembering where it has been: the chart allows several
    /// parents, so the same box can be reached by more than one route and an
    /// unguarded walk would revisit it forever.
    /// </summary>
    private static bool ReachesUpTo(int from, int target, Dictionary<int, List<int>> parentsOf)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == target) return true;
            if (!seen.Add(current)) continue;
            if (!parentsOf.TryGetValue(current, out var parents)) continue;

            foreach (var parent in parents) queue.Enqueue(parent);
        }

        return false;
    }
}
