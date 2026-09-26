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

    public async Task<string?> ValidateReferrerAsync(string? type, int? id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(type) || id is not > 0)
            return "Please select who referred this student.";

        var ok = type switch
        {
            ReferrerTypes.Teacher => await _db.Teachers.AnyAsync(t => t.Id == id && t.IsActive, ct),
            ReferrerTypes.Receptionist => await _db.Receptionists.AnyAsync(r => r.Id == id && r.IsActive, ct),
            _ => false
        };

        return ok ? null : "The selected referrer is not an active teacher or receptionist.";
    }

    /* ---------------------------------------------------------- credits */

    public async Task CreditAdmissionPaymentAsync(
        int admissionFeePaymentId, string? actor, CancellationToken ct = default)
    {
        // Once per payment: the unique index enforces it too.
        if (await _db.ReferralCredits.AnyAsync(r => r.AdmissionFeePaymentId == admissionFeePaymentId, ct))
            return;

        var payment = await _db.AdmissionFeePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == admissionFeePaymentId, ct);
        if (payment is null || payment.Amount <= 0m) return;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.Id == payment.StudentId)
            .Select(s => new { s.Id, s.Name, s.RegistrationId, s.Branch, s.ReferrerType, s.ReferrerId, s.ReferralPercent })
            .FirstOrDefaultAsync(ct);

        if (student?.ReferrerType is null || student.ReferrerId is not int referrerId
            || student.ReferralPercent is not > 0m)
            return;

        var percent = student.ReferralPercent.Value;
        var amount = Math.Round(payment.Amount * percent / 100m, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m) return;

        _db.ReferralCredits.Add(new ReferralCredit
        {
            StudentId = student.Id,
            RegistrationId = student.RegistrationId,
            AdmissionFeePaymentId = payment.Id,
            ReferrerType = student.ReferrerType,
            ReferrerId = referrerId,
            Branch = student.Branch,
            PaymentAmount = payment.Amount,
            Percent = percent,
            Amount = amount,
            CreditedOn = payment.PaidOn,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);

        // Best effort: a chat failure must never undo the credit already saved.
        try
        {
            await NotifyAsync(student.ReferrerType, referrerId, student.Name, student.RegistrationId,
                payment.Amount, percent, amount, actor, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Referral credit notice failed for payment {PaymentId}", payment.Id);
        }
    }

    private async Task NotifyAsync(
        string referrerType, int referrerId, string? studentName, string? registrationId,
        decimal paymentAmount, decimal percent, decimal amount, string? actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) return;

        var (referrerName, referrerLogin) = referrerType == ReferrerTypes.Teacher
            ? await _db.Teachers.AsNoTracking().Where(t => t.Id == referrerId)
                .Select(t => new ValueTuple<string?, string?>(t.Name, t.LoginId)).FirstOrDefaultAsync(ct)
            : await _db.Receptionists.AsNoTracking().Where(r => r.Id == referrerId)
                .Select(r => new ValueTuple<string?, string?>(r.Name, r.LoginId)).FirstOrDefaultAsync(ct);

        var destination = referrerType == ReferrerTypes.Teacher
            ? "added to their earnings"
            : "to be paid with their next monthly pay";

        var text = $"Admission referral: {referrerName} earns ₹{amount:0.##} ({percent:0.##}% of the ₹{paymentAmount:0.##} " +
                   $"admission fee collected from {studentName} ({registrationId})), {destination}.";

        // The referrer, plus everyone who oversees the money.
        var staff = await _db.Users.AsNoTracking()
            // Email, not LoginId: chat knows people by their email, and on older
            // rows LoginId holds whoever created the account, not its owner.
            .Where(u => u.IsActive && u.Roles != null && u.Email != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct);

        var recipients = staff
            .Where(u => UserRoles.Split(u.Roles).Any(r =>
                r is UserRoles.Admin or UserRoles.SuperAdmin or UserRoles.Editor))
            .Select(u => u.Email!)
            .Append(referrerLogin)
            .Where(l => !string.IsNullOrWhiteSpace(l)
                        && !string.Equals(l, actor, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var loginId in recipients)
        {
            var conversation = await _chat.StartOrGetDirectConversationAsync(actor, loginId!, ct);
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
