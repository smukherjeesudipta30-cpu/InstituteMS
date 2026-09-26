using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Application.Referrals;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Admission referral commission. A new admission names a teacher or
/// receptionist as its referrer; each admission-fee payment then credits that
/// person with the student's referral rate (from the slab table, copied onto
/// the student at admission) of the amount actually collected — never on money
/// not yet received. A teacher's credits count as earnings; a receptionist's
/// are paid on their next monthly payment.
/// </summary>
public class ReferralService : IReferralService
{
    private readonly AppDbContext _db;
    private readonly IChatService _chat;
    private readonly ILogger<ReferralService> _logger;

    public ReferralService(AppDbContext db, IChatService chat, ILogger<ReferralService> logger)
    {
        _db = db;
        _chat = chat;
        _logger = logger;
    }

    /* ------------------------------------------------------------- slabs */

    public async Task<ReferralSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var slabs = await _db.ReferralSlabs.AsNoTracking().OrderBy(s => s.MinFee).ToListAsync(ct);
        var last = slabs.OrderByDescending(s => s.ModifiedOn).FirstOrDefault();

        return new ReferralSettingsDto(
            slabs.Select(s => new ReferralSlabDto(s.MinFee, s.Percent)).ToList(),
            last?.ModifiedBy, last?.ModifiedOn);
    }

    public async Task<Result<ReferralSettingsDto>> SaveSettingsAsync(
        ReferralSettingsSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var slabs = request.Slabs ?? [];

        if (slabs.Any(s => s.MinFee <= 0m))
            return Result<ReferralSettingsDto>.Failure("Each slab's admission fee must be more than zero.");

        if (slabs.Any(s => s.Percent is < 0m or > 100m))
            return Result<ReferralSettingsDto>.Failure("Each percentage must be between 0 and 100.");

        if (slabs.Select(s => s.MinFee).Distinct().Count() != slabs.Count)
            return Result<ReferralSettingsDto>.Failure("Two slabs cannot start at the same admission fee.");

        // Replaced as a whole: the slab list is small and always edited together.
        _db.ReferralSlabs.RemoveRange(await _db.ReferralSlabs.ToListAsync(ct));

        var now = DateTime.Now;
        _db.ReferralSlabs.AddRange(slabs.OrderBy(s => s.MinFee).Select(s => new ReferralSlab
        {
            MinFee = s.MinFee,
            Percent = s.Percent,
            ModifiedBy = actor,
            ModifiedOn = now
        }));

        await _db.SaveChangesAsync(ct);
        return Result<ReferralSettingsDto>.Success(await GetSettingsAsync(ct));
    }

    public async Task<decimal> PercentForFeeAsync(decimal admissionFee, CancellationToken ct = default)
    {
        var slab = await _db.ReferralSlabs.AsNoTracking()
            .Where(s => s.MinFee <= admissionFee)
            .OrderByDescending(s => s.MinFee)
            .FirstOrDefaultAsync(ct);

        return slab?.Percent ?? 0m;
    }

    /* -------------------------------------------------------- referrers */

    public async Task<IReadOnlyList<ReferrerOptionDto>> GetReferrersAsync(CancellationToken ct = default)
    {
        var teachers = await _db.Teachers.AsNoTracking()
            .Where(t => t.IsActive && t.Name != null && t.Name != "")
            .Select(t => new ReferrerOptionDto(ReferrerTypes.Teacher, t.Id, t.Name!, t.Branch))
            .ToListAsync(ct);

        var receptionists = await _db.Receptionists.AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => new ReferrerOptionDto(ReferrerTypes.Receptionist, r.Id, r.Name, r.Branch))
            .ToListAsync(ct);

        return teachers.OrderBy(t => t.Name)
            .Concat(receptionists.OrderBy(r => r.Name))
            .ToList();
    }

    public async Task<Result<ReferrerSelection>> ResolveReferrersAsync(
        string? referrerType, IReadOnlyList<ReferrerRef>? referrers, CancellationToken ct = default)
    {
        var people = (referrers ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Type))
            .Select(r => new ReferrerRef(r.Type.Trim(), r.Id))
            .Distinct()
            .ToList();

        // A direct / walk-in admission is a deliberate choice: nobody is credited.
        if (referrerType == ReferrerTypes.None)
        {
            return people.Count == 0
                ? Result<ReferrerSelection>.Success(new ReferrerSelection(true, [], null))
                : Result<ReferrerSelection>.Failure(
                    "Choose either \"No referral\" or the people who referred the student, not both.");
        }

        if (people.Count == 0)
            return Result<ReferrerSelection>.Failure("Please select who referred this student.");

        if (people.Any(p => p.Type is not (ReferrerTypes.Teacher or ReferrerTypes.Receptionist)))
            return Result<ReferrerSelection>.Failure("A referrer must be a teacher or a receptionist.");

        var teacherIds = people.Where(p => p.Type == ReferrerTypes.Teacher).Select(p => p.Id).ToList();
        var receptionistIds = people.Where(p => p.Type == ReferrerTypes.Receptionist).Select(p => p.Id).ToList();

        var teachers = await _db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id) && t.IsActive)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var receptionists = await _db.Receptionists.AsNoTracking()
            .Where(r => receptionistIds.Contains(r.Id) && r.IsActive)
            .ToDictionaryAsync(r => r.Id, r => (string?)r.Name, ct);

        if (teachers.Count != teacherIds.Count || receptionists.Count != receptionistIds.Count)
            return Result<ReferrerSelection>.Failure(
                "Every selected referrer must be an active teacher or receptionist.");

        var summary = string.Join(", ", people.Select(p => p.Type == ReferrerTypes.Teacher
            ? $"{teachers[p.Id]} (Teacher)"
            : $"{receptionists[p.Id]} (Receptionist)"));

        return Result<ReferrerSelection>.Success(new ReferrerSelection(
            false, people, summary.Length > 500 ? summary[..497] + "..." : summary));
    }

    /* ---------------------------------------------------------- credits */

    public async Task CreditAdmissionPaymentAsync(
        int admissionFeePaymentId, string? actor, CancellationToken ct = default)
    {
        var payment = await _db.AdmissionFeePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == admissionFeePaymentId, ct);
        if (payment is null || payment.Amount <= 0m) return;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == payment.StudentId)
            .Select(s => new { s.Id, s.Name, s.RegistrationId, s.Branch, s.ReferralPercent })
            .FirstOrDefaultAsync(ct);
        if (student?.ReferralPercent is not > 0m) return;

        var referrers = await _db.StudentReferrers.AsNoTracking()
            .Where(r => r.StudentId == student.Id)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        if (referrers.Count == 0) return;

        // Once per payment: the unique index (payment + person) enforces it too.
        if (await _db.ReferralCredits.AnyAsync(r => r.AdmissionFeePaymentId == payment.Id, ct))
            return;

        var percent = student.ReferralPercent.Value;
        var total = Math.Round(payment.Amount * percent / 100m, 2, MidpointRounding.AwayFromZero);
        if (total <= 0m) return;

        /* Split equally. Shares are whole paise; any paisa left over by the
           division goes to the first referrers listed, so the shares always add
           up to exactly the slab amount - never a paisa more or less. */
        var paise = (long)(total * 100m);
        var baseShare = paise / referrers.Count;
        var leftover = paise % referrers.Count;
        var shares = referrers
            .Select((r, i) => (Referrer: r, Amount: (baseShare + (i < leftover ? 1 : 0)) / 100m))
            .Where(s => s.Amount > 0m)
            .ToList();

        foreach (var (referrer, amount) in shares)
        {
            _db.ReferralCredits.Add(new ReferralCredit
            {
                StudentId = student.Id,
                RegistrationId = student.RegistrationId,
                AdmissionFeePaymentId = payment.Id,
                ReferrerType = referrer.ReferrerType,
                ReferrerId = referrer.ReferrerId,
                Branch = student.Branch,
                PaymentAmount = payment.Amount,
                Percent = percent,
                Amount = amount,
                CreditedOn = payment.PaidOn,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });
        }

        await _db.SaveChangesAsync(ct);

        // Best effort: a chat failure must never undo the credits already saved.
        try
        {
            await NotifyAsync(shares, student.Name, student.RegistrationId, payment.Amount, percent, total, actor, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Referral credit notice failed for payment {PaymentId}", payment.Id);
        }
    }

    private async Task NotifyAsync(
        IReadOnlyList<(StudentReferrer Referrer, decimal Amount)> shares,
        string? studentName, string? registrationId,
        decimal paymentAmount, decimal percent, decimal total, string? actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) return;

        var teacherIds = shares.Where(s => s.Referrer.ReferrerType == ReferrerTypes.Teacher)
            .Select(s => s.Referrer.ReferrerId).ToList();
        var receptionistIds = shares.Where(s => s.Referrer.ReferrerType == ReferrerTypes.Receptionist)
            .Select(s => s.Referrer.ReferrerId).ToList();

        var teachers = await _db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => (Name: t.Name, Login: t.LoginId), ct);
        var receptionists = await _db.Receptionists.AsNoTracking().Where(r => receptionistIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => (Name: (string?)r.Name, Login: r.LoginId), ct);

        (string? Name, string? Login) Who(StudentReferrer r) => r.ReferrerType == ReferrerTypes.Teacher
            ? teachers.GetValueOrDefault(r.ReferrerId)
            : receptionists.GetValueOrDefault(r.ReferrerId);

        var lines = shares.Select(s => s.Referrer.ReferrerType == ReferrerTypes.Teacher
            ? $"• {Who(s.Referrer).Name}: ₹{s.Amount:0.##}, added to their earnings"
            : $"• {Who(s.Referrer).Name}: ₹{s.Amount:0.##}, to be paid with their monthly pay");

        var splitNote = shares.Count > 1 ? $", split equally between {shares.Count} referrers" : string.Empty;

        var text = $"Admission referral: ₹{total:0.##} ({percent:0.##}% of the ₹{paymentAmount:0.##} admission fee " +
                   $"collected from {studentName} ({registrationId})){splitNote}.\n" +
                   string.Join("\n", lines);

        // The referrers, plus everyone who oversees the money.
        var staff = await _db.Users.AsNoTracking()
            // Email, not LoginId: chat knows people by their email, and on older
            // rows LoginId holds whoever created the account, not its owner.
            .Where(u => u.IsActive && u.Roles != null && u.Email != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct);

        var recipients = staff
            .Where(u => UserRoles.Split(u.Roles).Any(r =>
                r is UserRoles.Admin or UserRoles.SuperAdmin or UserRoles.Editor))
            .Select(u => u.Email)
            .Concat(shares.Select(s => Who(s.Referrer).Login))
            .Where(l => !string.IsNullOrWhiteSpace(l)
                        && !string.Equals(l, actor, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var email in recipients)
        {
            var conversation = await _chat.StartOrGetDirectConversationAsync(actor, email!, ct);
            if (!conversation.Succeeded) continue;

            await _chat.SendMessageAsync(actor, conversation.Value, new SendMessageRequest { Body = text }, ct);
        }
    }

    public async Task<IReadOnlyList<ReferralCreditDto>> GetCreditsAsync(
        DateOnly? from, DateOnly? to, string? referrerType, int? referrerId, CancellationToken ct = default)
    {
        var q = _db.ReferralCredits.AsNoTracking().AsQueryable();
        if (from is { } f) q = q.Where(r => r.CreditedOn >= f);
        if (to is { } t) q = q.Where(r => r.CreditedOn <= t);
        if (!string.IsNullOrWhiteSpace(referrerType)) q = q.Where(r => r.ReferrerType == referrerType);
        if (referrerId is > 0) q = q.Where(r => r.ReferrerId == referrerId);

        var rows = await q.OrderByDescending(r => r.CreditedOn).ThenByDescending(r => r.Id).ToListAsync(ct);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await _db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var teacherNames = await _db.Teachers.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var receptionistNames = await _db.Receptionists.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => (string?)x.Name, ct);

        return rows.Select(r => new ReferralCreditDto(
            r.Id, r.CreditedOn, r.RegistrationId, students.GetValueOrDefault(r.StudentId),
            r.ReferrerType, r.ReferrerId,
            r.ReferrerType == ReferrerTypes.Teacher
                ? teacherNames.GetValueOrDefault(r.ReferrerId)
                : receptionistNames.GetValueOrDefault(r.ReferrerId),
            r.Branch, r.PaymentAmount, r.Percent, r.Amount, r.ReceptionistPaymentId is not null)).ToList();
    }
}
