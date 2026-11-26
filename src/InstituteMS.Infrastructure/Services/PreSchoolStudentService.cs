using InstituteMS.Application.Common;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// PreSchool admissions, monthly fees and receipts. Reads and writes
/// PreSchool* tables only.
/// </summary>
/// <remarks>
/// <para>What is due is worked out, never stored: every month from the joining
/// month to this one (or to the month the child completed or left) costs the
/// class's active fee heads less the child's discount. A month is paid once a
/// receipt that is not cancelled has a line for it.</para>
///
/// <para>An unpaid month is priced at today's fees; a paid one keeps what its
/// receipt charged, so changing a fee never rewrites an old receipt.</para>
/// </remarks>
public class PreSchoolStudentService : IPreSchoolStudentService
{
    private const string KindAdmission = "Admission";
    private const string KindMonthly = "Monthly";
    private const string KindCharge = "Charge";
    private const string KindDiscount = "Discount";

    /// <summary>How far ahead a parent may pay.</summary>
    private const int AdvanceMonths = 12;

    private readonly AppDbContext _db;
    private readonly IStudentMailer? _mailer;

    public PreSchoolStudentService(AppDbContext db, IStudentMailer? mailer = null)
    {
        _db = db;
        _mailer = mailer;
    }

    /* --------------------------------------------------------- students */

    public async Task<IReadOnlyList<PreSchoolStudentListItemDto>> ListAsync(
        string? search, int? classId, string? status, CancellationToken ct = default)
    {
        var query = _db.PreSchoolStudents.AsNoTracking().Include(s => s.Class).AsQueryable();
        if (classId is > 0) query = query.Where(s => s.ClassId == classId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());
            int.TryParse(digits, out var no);
            query = query.Where(s => s.ChildName.Contains(q) || s.ParentName.Contains(q) || s.Phone.Contains(q)
                                     || (no > 0 && s.AdmissionNo == no));
        }

        var students = await query.OrderBy(s => s.Class!.SortOrder).ThenBy(s => s.ChildName).Take(1000).ToListAsync(ct);
        var ctx = await LoadContextAsync(students.Select(s => s.Id).ToList(), ct);

        return students.Select(s =>
        {
            var fee = MonthlyFee(s, ctx);
            var due = DueMonths(s, ctx, fee);
            var admissionDue = s.Status == PreSchoolStudentStatus.Active && !ctx.AdmissionPaid.Contains(s.Id) ? s.Class?.AdmissionFee ?? 0 : 0;
            var instalmentDue = InstalmentDue(s.Id, ctx);
            return new PreSchoolStudentListItemDto(
                s.Id, AdmissionCode(s, ctx), s.ChildName, s.ClassId, s.Class?.Name ?? "", s.ParentName, s.Phone,
                s.JoinedOn, s.Status, fee, due.Count, admissionDue, fee * due.Count + admissionDue + instalmentDue,
                s.Section, instalmentDue);
        }).ToList();
    }

    public async Task<Result<PreSchoolStudentDetailDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var s = await _db.PreSchoolStudents.AsNoTracking().Include(x => x.Class).Include(x => x.ReferredBy)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Result<PreSchoolStudentDetailDto>.Failure("That child is not on the list.", ResultStatus.NotFound);

        return Result<PreSchoolStudentDetailDto>.Success(ToDetail(s, await LoadContextAsync([s.Id], ct)));
    }

    public async Task<Result<PreSchoolStudentDetailDto>> AdmitAsync(PreSchoolStudentSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var cls = await _db.PreSchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClassId, ct);
        if (cls is null) return Result<PreSchoolStudentDetailDto>.Failure("That class no longer exists.", ResultStatus.NotFound);
        if (!cls.IsActive) return Result<PreSchoolStudentDetailDto>.Failure($"{cls.Name} is not taking admissions.");

        var ageProblem = CheckAge(cls, request.DateOfBirth, request.JoinedOn);
        if (ageProblem is not null) return Result<PreSchoolStudentDetailDto>.Failure(ageProblem);

        if (request.ReferenceType is null)
            return Result<PreSchoolStudentDetailDto>.Failure("Choose who referred the child (a teacher or receptionist), or No reference.");
        var referenceProblem = await CheckReferrerAsync(request, ct);
        if (referenceProblem is not null) return Result<PreSchoolStudentDetailDto>.Failure(referenceProblem);

        var entity = new PreSchoolStudent
        {
            AdmissionNo = await NextAsync("PreSchoolAdmissionNo", ct),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };
        Apply(entity, request);
        ApplyReference(entity, request);
        _db.PreSchoolStudents.Add(entity);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(entity.Id, ct);
    }

    public async Task<Result<PreSchoolStudentDetailDto>> UpdateAsync(int id, PreSchoolStudentSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var entity = await _db.PreSchoolStudents.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return Result<PreSchoolStudentDetailDto>.Failure("That child is not on the list.", ResultStatus.NotFound);
        if (!await _db.PreSchoolClasses.AnyAsync(c => c.Id == request.ClassId, ct))
            return Result<PreSchoolStudentDetailDto>.Failure("That class no longer exists.", ResultStatus.NotFound);

        Apply(entity, request);
        if (request.ReferenceType is not null)
        {
            var referenceProblem = await CheckReferrerAsync(request, ct);
            if (referenceProblem is not null) return Result<PreSchoolStudentDetailDto>.Failure(referenceProblem);
            ApplyReference(entity, request);
        }
        entity.ModifiedBy = actor;
        entity.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /* ------------------------------------------------------- references */

    public async Task<IReadOnlyList<PreSchoolReferrerDto>> ReferrersAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return await _db.PreSchoolStaff.AsNoTracking()
            .Where(s => (s.Role == PreSchoolStaffRoles.Teacher || s.Role == PreSchoolStaffRoles.Receptionist)
                        && (s.LeftOn == null || s.LeftOn >= today))
            .OrderBy(s => s.Role).ThenBy(s => s.Name)
            .Select(s => new PreSchoolReferrerDto(s.Id, s.Name, s.Role))
            .ToListAsync(ct);
    }

    /// <summary>Only a PreSchool teacher or receptionist on the staff list can be a referrer.</summary>
    private async Task<string?> CheckReferrerAsync(PreSchoolStudentSaveRequest r, CancellationToken ct)
    {
        if (r.ReferenceType != PreSchoolReferenceTypes.Staff) return null;
        var staff = await _db.PreSchoolStaff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.ReferredByStaffId, ct);
        if (staff is null) return "That referrer is not on the PreSchool staff list.";
        return staff.Role is PreSchoolStaffRoles.Teacher or PreSchoolStaffRoles.Receptionist
            ? null
            : "Only a PreSchool teacher or receptionist can be named as the referrer.";
    }

    private static void ApplyReference(PreSchoolStudent e, PreSchoolStudentSaveRequest r)
    {
        e.ReferenceType = r.ReferenceType;
        e.ReferredByStaffId = r.ReferenceType == PreSchoolReferenceTypes.Staff ? r.ReferredByStaffId : null;
    }

    /// <summary>
    /// The referrer's share of a payment on the child's admission receipt; null
    /// when there is nothing to credit (no referrer, not the admission receipt, or 0%).
    /// </summary>
    private async Task<PreSchoolReferralCredit?> ReferralCreditAsync(
        PreSchoolStudent s, bool isAdmissionReceipt, PreSchoolReceiptPayment payment, string? actor, CancellationToken ct)
    {
        if (!isAdmissionReceipt || s.ReferenceType != PreSchoolReferenceTypes.Staff || s.ReferredByStaffId is not int staffId) return null;
        var percent = await _db.PreSchoolSettings.AsNoTracking().Select(x => (decimal?)x.ReferralPercent).FirstOrDefaultAsync(ct) ?? 5m;
        var amount = Math.Round(payment.Amount * percent / 100m, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) return null;
        return new PreSchoolReferralCredit
        {
            StudentId = s.Id, ReceiptPayment = payment, StaffId = staffId, PaymentAmount = payment.Amount,
            Percent = percent, Amount = amount, CreditedOn = payment.PaidOn, CreatedBy = actor, CreatedOn = DateTime.Now
        };
    }

    /// <summary>The class's age limits, checked at admission only: a child keeps their place as they grow.</summary>
    private static string? CheckAge(PreSchoolClass cls, DateOnly dob, DateOnly joinedOn)
    {
        var months = (joinedOn.Year - dob.Year) * 12 + joinedOn.Month - dob.Month - (joinedOn.Day < dob.Day ? 1 : 0);
        if (cls.MinAgeMonths is int min && months < min)
            return $"The child is too young for {cls.Name} on the joining date (the class starts at {min / 12} yrs {min % 12} mths).";
        if (cls.MaxAgeMonths is int max && months > max)
            return $"The child is older than {cls.Name} allows on the joining date (up to {max / 12} yrs {max % 12} mths).";
        return null;
    }

    private static void Apply(PreSchoolStudent e, PreSchoolStudentSaveRequest r)
    {
        e.ChildName = r.ChildName.Trim();
        e.DateOfBirth = r.DateOfBirth;
        e.Gender = Clean(r.Gender);
        e.BloodGroup = Clean(r.BloodGroup);
        e.FatherName = Clean(r.FatherName);
        e.MotherName = Clean(r.MotherName);
        e.ParentName = r.ParentName.Trim();
        e.Phone = r.Phone.Trim();
        e.EmergencyPhone = Clean(r.EmergencyPhone);
        e.Email = Clean(r.Email);
        e.Address = Clean(r.Address);
        e.GuardianRelation = Clean(r.GuardianRelation);
        e.Guardian2Name = Clean(r.Guardian2Name);
        e.Guardian2Relation = Clean(r.Guardian2Relation);
        e.Guardian2Phone = Clean(r.Guardian2Phone);
        e.HeightCm = r.HeightCm;
        e.WeightKg = r.WeightKg;
        e.ClassId = r.ClassId;
        e.JoinedOn = r.JoinedOn;
        e.MonthlyDiscount = r.MonthlyDiscount;
        e.Status = r.Status;
        e.EndedOn = r.Status == PreSchoolStudentStatus.Active ? null : r.EndedOn;
        e.Notes = Clean(r.Notes);
        e.Section = Clean(r.Section)?.ToUpperInvariant();
    }

    /* ------------------------------------------------------------- fees */

    public async Task<Result<PreSchoolReceiptDto>> CollectAsync(int studentId, PreSchoolCollectRequest request, string? actor, CancellationToken ct = default)
    {
        var s = await _db.PreSchoolStudents.AsNoTracking().Include(x => x.Class).FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return Result<PreSchoolReceiptDto>.Failure("That child is not on the list.", ResultStatus.NotFound);

        var ctx = await LoadContextAsync([s.Id], ct);
        var heads = ctx.HeadsByClass.GetValueOrDefault(s.ClassId) ?? [];
        var paid = ctx.PaidMonths.GetValueOrDefault(s.Id) ?? [];
        var lines = new List<PreSchoolReceiptLine>();

        if (request.IncludeAdmission)
        {
            if (ctx.AdmissionPaid.Contains(s.Id))
                return Result<PreSchoolReceiptDto>.Failure("The admission fee has already been received.", ResultStatus.Conflict);
            var amount = request.AdmissionAmount ?? s.Class!.AdmissionFee;
            if (amount <= 0) return Result<PreSchoolReceiptDto>.Failure($"{s.Class!.Name} has no admission fee set; enter the amount.");
            lines.Add(new PreSchoolReceiptLine { Kind = KindAdmission, Description = $"Admission fee — {s.Class!.Name}", Amount = amount });
        }

        var months = request.Months.Select(FirstOfMonth).Distinct().OrderBy(m => m).ToList();
        if (months.Count > 0)
        {
            if (heads.Count == 0) return Result<PreSchoolReceiptDto>.Failure($"{s.Class!.Name} has no monthly fees set yet.");

            var start = FirstOfMonth(s.JoinedOn);
            var last = s.Status == PreSchoolStudentStatus.Active
                ? FirstOfMonth(DateOnly.FromDateTime(DateTime.Today)).AddMonths(AdvanceMonths)
                : FirstOfMonth(s.EndedOn ?? s.JoinedOn);

            foreach (var m in months)
            {
                if (m < start) return Result<PreSchoolReceiptDto>.Failure($"{m:MMM yyyy} is before {s.ChildName} joined.");
                if (m > last) return Result<PreSchoolReceiptDto>.Failure($"{m:MMM yyyy} is too far ahead to collect.");
                if (paid.Contains(m)) return Result<PreSchoolReceiptDto>.Failure($"{m:MMM yyyy} is already paid.", ResultStatus.Conflict);

                var monthTotal = 0m;
                foreach (var h in heads)
                {
                    lines.Add(new PreSchoolReceiptLine { Kind = KindMonthly, ForMonth = m, Description = $"{h.Name} — {m:MMM yyyy}", Amount = h.MonthlyAmount });
                    monthTotal += h.MonthlyAmount;
                }

                var discount = Math.Min(s.MonthlyDiscount, monthTotal);
                if (discount > 0)
                    lines.Add(new PreSchoolReceiptLine { Kind = KindMonthly, ForMonth = m, Description = $"Discount — {m:MMM yyyy}", Amount = -discount });
            }
        }

        // The receipt's other rows, in the printed order; a zero amount is simply not printed.
        foreach (var head in PreSchoolChargeHeads.Labels.Keys)
        {
            var c = request.Charges.FirstOrDefault(x => x.Head == head);
            if (c is null || c.Amount <= 0) continue;
            var note = Clean(c.Note);
            lines.Add(new PreSchoolReceiptLine
            {
                Kind = KindCharge,
                ChargeHead = head,
                Description = note is null ? PreSchoolChargeHeads.Labels[head] : $"{PreSchoolChargeHeads.Labels[head]} — {note}",
                Amount = c.Amount
            });
        }

        var gross = lines.Sum(l => l.Amount);
        if (gross <= 0) return Result<PreSchoolReceiptDto>.Failure("There is nothing to collect for what was ticked.");

        // The discount comes off the whole receipt, then the special offer off what is left.
        var percent = request.DiscountPercent ?? 0m;
        var percentOff = percent > 0 ? Math.Round(gross * percent / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        if (percentOff > 0)
            lines.Add(new PreSchoolReceiptLine
            {
                Kind = KindDiscount, ChargeHead = "Discount", Description = $"Discount ({percent:0.##}%)", Amount = -percentOff
            });
        var offer = request.SpecialOffer ?? 0m;
        if (offer > gross - percentOff)
            return Result<PreSchoolReceiptDto>.Failure($"The special offer is more than the {gross - percentOff:0.##} left to pay.");
        if (offer > 0)
        {
            var note = Clean(request.OfferNote);
            lines.Add(new PreSchoolReceiptLine
            {
                Kind = KindDiscount, ChargeHead = "SpecialOffer",
                Description = note is null ? "Special offer" : $"Special offer — {note}", Amount = -offer
            });
        }

        var total = lines.Sum(l => l.Amount);
        var byCheque = request.Mode is "Cheque" or "DD";

        // Paying in instalments: the admission package (not monthly fees alone) may be paid in up to four parts.
        var paidNow = request.PaidNow is decimal part && part < total ? part : total;
        if (paidNow < total)
        {
            if (!lines.Any(l => l.Kind is KindAdmission or KindCharge))
                return Result<PreSchoolReceiptDto>.Failure("Only the admission and registration fees can be paid in instalments; monthly fees are paid in full.");
            if (paidNow <= 0)
                return Result<PreSchoolReceiptDto>.Failure("Enter the first instalment received.");
        }
        var receivedOn = request.ReceivedOn ?? DateOnly.FromDateTime(DateTime.Today);

        var receipt = new PreSchoolReceipt
        {
            ReceiptNo = await NextAsync("PreSchoolReceiptNo", ct),
            StudentId = s.Id,
            ReceivedOn = receivedOn,
            Mode = request.Mode,
            Reference = request.Mode == "Cash" ? null : Clean(request.Reference),
            ChequeDate = byCheque ? request.ChequeDate : null,
            DrawnOn = byCheque ? Clean(request.DrawnOn) : null,
            Total = total,
            Notes = Clean(request.Notes),
            CreatedBy = actor,
            CreatedOn = DateTime.Now,
            Lines = lines
        };
        var firstPayment = new PreSchoolReceiptPayment
        {
            InstalmentNo = 1, PaidOn = receivedOn, Amount = paidNow, Mode = request.Mode,
            Reference = request.Mode == "Cash" ? null : Clean(request.Reference),
            CollectedBy = actor, CreatedOn = DateTime.Now
        };
        receipt.Payments.Add(firstPayment);
        _db.PreSchoolReceipts.Add(receipt);
        if (await ReferralCreditAsync(s, lines.Any(l => l.Kind == KindAdmission), firstPayment, actor, ct) is { } credit)
            _db.PreSchoolReferralCredits.Add(credit);
        await _db.SaveChangesAsync(ct);

        return await GetReceiptAsync(receipt.Id, ct);
    }

    public async Task<Result<PreSchoolReceiptDto>> GetReceiptAsync(int receiptId, CancellationToken ct = default)
    {
        var r = await _db.PreSchoolReceipts.AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .Include(x => x.Student).ThenInclude(s => s!.Class)
            .FirstOrDefaultAsync(x => x.Id == receiptId, ct);
        if (r is null) return Result<PreSchoolReceiptDto>.Failure("That receipt does not exist.", ResultStatus.NotFound);

        var settings = await _db.PreSchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var prefix = settings?.ReceiptPrefix ?? "APS";
        var s = r.Student!;

        return Result<PreSchoolReceiptDto>.Success(new PreSchoolReceiptDto(
            r.Id, ReceiptCode(prefix, r.ReceiptNo), r.ReceivedOn, r.Mode, r.Reference, r.Total,
            NumberToWords.Convert(r.Total), r.Notes, r.IsCancelled, r.CreatedBy,
            s.Id, AdmissionCode(prefix, s.AdmissionNo), s.ChildName, s.Class?.Name ?? "", s.ParentName, s.Phone,
            settings?.SchoolName ?? "Avijan PreSchool", settings?.Address, settings?.Phone,
            r.Lines.OrderBy(l => l.Kind == KindAdmission ? 0 : l.Kind == KindMonthly ? 1 : l.Kind == KindCharge ? 2 : 3).ThenBy(l => l.ForMonth).ThenBy(l => l.Amount < 0).ThenBy(l => l.Id)
                .Select(l => new PreSchoolReceiptLineDto(l.Kind, l.ForMonth, l.Description, l.Amount, l.ChargeHead)).ToList(),
            s.Section, s.FatherName, s.MotherName, s.Address, r.ChequeDate, r.DrawnOn,
            Paid(r), Balance(r), NumberToWords.Convert(Paid(r)), Payments(r)));
    }

    /* ------------------------------------------------------------ email */

    public async Task<Result<PreSchoolReceiptMailResultDto>> EmailReceiptAsync(int receiptId, byte[]? pdf, string? actor, CancellationToken ct = default)
    {
        var found = await GetReceiptAsync(receiptId, ct);
        if (!found.Succeeded) return Result<PreSchoolReceiptMailResultDto>.Failure(found.Error!, ResultStatus.NotFound);
        var r = found.Value!;
        if (r.IsCancelled) return Result<PreSchoolReceiptMailResultDto>.Success(new(false, "A cancelled receipt is not emailed."));

        var email = await _db.PreSchoolStudents.AsNoTracking().Where(s => s.Id == r.StudentId).Select(s => s.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email))
            return Result<PreSchoolReceiptMailResultDto>.Success(new(false, $"No guardian email on {r.ChildName}'s record, so the receipt was not emailed."));
        if (_mailer is null)
            return Result<PreSchoolReceiptMailResultDto>.Success(new(false, "Email is not set up on this server."));

        var sent = await _mailer.SendPreSchoolReceiptAsync(new PreSchoolReceiptMailRequest(
            email.Trim(), r.ParentName, r.ChildName, r.ClassName, r.ReceiptCode, r.ReceivedOn,
            r.Lines.Select(l => (l.Description, l.Amount)).ToList(), r.Total, r.Paid, r.Balance, r.Mode,
            r.SchoolName, r.SchoolAddress, r.SchoolPhone,
            pdf, $"FeeReceipt_{r.ReceiptCode.Replace('/', '-')}.pdf"), actor, ct);

        return Result<PreSchoolReceiptMailResultDto>.Success(sent
            ? new(true, $"Receipt emailed to {email.Trim()}.", email.Trim())
            : new(false, "The receipt could not be emailed just now. Try again with the email button.", email.Trim()));
    }

    /* ------------------------------------------------------ instalments */

    public async Task<Result<PreSchoolReceiptDto>> AddPaymentAsync(int receiptId, PreSchoolPaymentRequest request, string? actor, CancellationToken ct = default)
    {
        var r = await _db.PreSchoolReceipts.Include(x => x.Payments).Include(x => x.Lines).Include(x => x.Student)
            .FirstOrDefaultAsync(x => x.Id == receiptId, ct);
        if (r is null) return Result<PreSchoolReceiptDto>.Failure("That receipt does not exist.", ResultStatus.NotFound);
        if (r.IsCancelled) return Result<PreSchoolReceiptDto>.Failure("That receipt is cancelled.", ResultStatus.Conflict);

        var balance = Balance(r);
        if (balance <= 0) return Result<PreSchoolReceiptDto>.Failure("This receipt is already paid in full.", ResultStatus.Conflict);
        if (request.Amount > balance)
            return Result<PreSchoolReceiptDto>.Failure($"Only {balance:0.##} is left to pay on this receipt.");

        var next = r.Payments.Count == 0 ? 1 : r.Payments.Max(p => p.InstalmentNo) + 1;
        if (next > PreSchoolReceiptPayment.MaxInstalments)
            return Result<PreSchoolReceiptDto>.Failure($"A receipt can be paid in at most {PreSchoolReceiptPayment.MaxInstalments} instalments.");
        if (next == PreSchoolReceiptPayment.MaxInstalments && request.Amount < balance)
            return Result<PreSchoolReceiptDto>.Failure($"This is the last instalment allowed: it must clear the {balance:0.##} left.");

        var paidOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Today);
        if (paidOn < r.ReceivedOn) return Result<PreSchoolReceiptDto>.Failure("An instalment cannot be dated before the receipt.");

        var payment = new PreSchoolReceiptPayment
        {
            InstalmentNo = next, PaidOn = paidOn, Amount = request.Amount, Mode = request.Mode,
            Reference = request.Mode == "Cash" ? null : Clean(request.Reference), Notes = Clean(request.Notes),
            CollectedBy = actor, CreatedOn = DateTime.Now
        };
        r.Payments.Add(payment);
        if (await ReferralCreditAsync(r.Student!, r.Lines.Any(l => l.Kind == KindAdmission), payment, actor, ct) is { } credit)
            _db.PreSchoolReferralCredits.Add(credit);
        await _db.SaveChangesAsync(ct);

        return await GetReceiptAsync(receiptId, ct);
    }

    /// <summary>Received so far. A receipt from before instalments has no payment rows: it was paid in full.</summary>
    private static decimal Paid(PreSchoolReceipt r) => r.Payments.Count == 0 ? r.Total : r.Payments.Sum(p => p.Amount);

    /// <summary>Still to come on a receipt; nothing on a cancelled one.</summary>
    private static decimal Balance(PreSchoolReceipt r) => r.IsCancelled ? 0 : Math.Max(0, r.Total - Paid(r));

    private static List<PreSchoolPaymentDto> Payments(PreSchoolReceipt r) => r.Payments.OrderBy(p => p.InstalmentNo)
        .Select(p => new PreSchoolPaymentDto(p.Id, p.InstalmentNo, p.PaidOn, p.Amount, p.Mode, p.Reference, p.Notes, p.CollectedBy))
        .ToList();

    private static decimal InstalmentDue(int studentId, FeeContext ctx) =>
        (ctx.Receipts.GetValueOrDefault(studentId) ?? []).Sum(Balance);

    /// <summary>A receipt made in error is cancelled, not deleted: its number stays used and its months become due again.</summary>
    public async Task<Result<PreSchoolReceiptDto>> CancelReceiptAsync(int receiptId, string? actor, CancellationToken ct = default)
    {
        var r = await _db.PreSchoolReceipts.FirstOrDefaultAsync(x => x.Id == receiptId, ct);
        if (r is null) return Result<PreSchoolReceiptDto>.Failure("That receipt does not exist.", ResultStatus.NotFound);
        if (r.IsCancelled) return Result<PreSchoolReceiptDto>.Failure("That receipt is already cancelled.", ResultStatus.Conflict);

        r.IsCancelled = true;
        r.CancelledBy = actor;
        r.CancelledOn = DateTime.Now;
        // The referrer's share of a cancelled receipt is taken back, unless a salary has already paid it.
        var unpaid = await _db.PreSchoolReferralCredits
            .Where(c => c.ReceiptPayment!.ReceiptId == receiptId && c.SalaryPaymentId == null).ToListAsync(ct);
        _db.PreSchoolReferralCredits.RemoveRange(unpaid);
        await _db.SaveChangesAsync(ct);

        return await GetReceiptAsync(receiptId, ct);
    }

    /* ---------------------------------------------------------- pending */

    public async Task<PreSchoolPendingReportDto> PendingAsync(int? classId, int minMonths, CancellationToken ct = default)
    {
        var query = _db.PreSchoolStudents.AsNoTracking().Include(s => s.Class)
            .Where(s => s.Status == PreSchoolStudentStatus.Active);
        if (classId is > 0) query = query.Where(s => s.ClassId == classId);

        var students = await query.OrderBy(s => s.Class!.SortOrder).ThenBy(s => s.ChildName).ToListAsync(ct);
        var ctx = await LoadContextAsync(students.Select(s => s.Id).ToList(), ct);

        var rows = new List<PreSchoolPendingRowDto>();
        foreach (var s in students)
        {
            var fee = MonthlyFee(s, ctx);
            var due = DueMonths(s, ctx, fee);
            var admissionDue = !ctx.AdmissionPaid.Contains(s.Id) ? s.Class!.AdmissionFee : 0;
            var instalmentDue = InstalmentDue(s.Id, ctx);
            if (due.Count < Math.Max(1, minMonths) && !(minMonths <= 1 && (admissionDue > 0 || instalmentDue > 0))) continue;

            rows.Add(new PreSchoolPendingRowDto(
                s.Id, AdmissionCode(s, ctx), s.ChildName, s.Class?.Name ?? "", s.ParentName, s.Phone,
                due, fee, admissionDue, fee * due.Count + admissionDue + instalmentDue, instalmentDue));
        }

        return new PreSchoolPendingReportDto(rows, rows.Count, rows.Sum(r => r.Months.Count), rows.Sum(r => r.Amount));
    }

    /* ------------------------------------------------------- admissions */

    public async Task<Result<PreSchoolAdmissionReportDto>> AdmissionsAsync(DateOnly start, DateOnly end, int? classId, CancellationToken ct = default)
    {
        if (end < start) return Result<PreSchoolAdmissionReportDto>.Failure("The end date is before the start date.");
        if (end.DayNumber - start.DayNumber > 3700) return Result<PreSchoolAdmissionReportDto>.Failure("Pick at most about ten years at a time.");

        var query = _db.PreSchoolStudents.AsNoTracking().Include(s => s.Class)
            .Where(s => s.JoinedOn >= start && s.JoinedOn <= end);
        if (classId is > 0) query = query.Where(s => s.ClassId == classId);

        var students = await query.OrderBy(s => s.JoinedOn).ThenBy(s => s.AdmissionNo).ToListAsync(ct);
        var ctx = await LoadContextAsync(students.Select(s => s.Id).ToList(), ct);

        // The admission fee actually received for each child, from receipts that stand.
        var feeByStudent = ctx.Receipts.Values.SelectMany(r => r)
            .Where(r => !r.IsCancelled)
            .SelectMany(r => r.Lines.Where(l => l.Kind == KindAdmission).Select(l => (r.StudentId, l.Amount)))
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // The admission receipt(s) of each child: what was billed, received so far and still to come.
        var admissionReceipts = ctx.Receipts.Values.SelectMany(r => r)
            .Where(r => !r.IsCancelled && r.Lines.Any(l => l.Kind == KindAdmission))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => (Billed: g.Sum(r => r.Total), Paid: g.Sum(Paid), Balance: g.Sum(Balance)));

        var firstMonth = FirstOfMonth(start);
        var monthCount = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        var months = Enumerable.Range(0, monthCount).Select(i =>
        {
            var month = firstMonth.AddMonths(i);
            var inMonth = students.Where(s => s.JoinedOn.Year == month.Year && s.JoinedOn.Month == month.Month).ToList();
            var rows = inMonth.Select(s =>
            {
                var bill = admissionReceipts.GetValueOrDefault(s.Id);
                return new PreSchoolAdmissionRowDto(
                    s.Id, AdmissionCode(s, ctx), s.ChildName, s.Class?.Name ?? "", s.ParentName, s.Phone, s.JoinedOn, s.Status,
                    feeByStudent.GetValueOrDefault(s.Id), s.Section, bill.Billed, bill.Paid, bill.Balance, s.DateOfBirth);
            }).ToList();
            return new PreSchoolAdmissionMonthDto(
                month, rows.Count, rows.Sum(r => r.AdmissionFeeReceived),
                rows.GroupBy(r => r.ClassName).ToDictionary(g => g.Key, g => g.Count()),
                rows);
        }).ToList();

        var all = months.SelectMany(m => m.Children).ToList();
        return Result<PreSchoolAdmissionReportDto>.Success(new PreSchoolAdmissionReportDto(
            start.Year, students.Count, months.Sum(m => m.AdmissionFees), months,
            start, end, all.Sum(r => r.Billed), all.Sum(r => r.Paid), all.Sum(r => r.Balance),
            all.GroupBy(r => r.ClassName).ToDictionary(g => g.Key, g => g.Count())));
    }

    /* ---------------------------------------------------------- helpers */

    /// <summary>What working out dues needs, loaded once for a batch of children.</summary>
    private sealed record FeeContext(
        string Prefix,
        Dictionary<int, List<PreSchoolFeeHead>> HeadsByClass,
        Dictionary<int, HashSet<DateOnly>> PaidMonths,
        Dictionary<int, Dictionary<DateOnly, (decimal Amount, int ReceiptNo)>> PaidDetail,
        HashSet<int> AdmissionPaid,
        Dictionary<int, List<PreSchoolReceipt>> Receipts);

    private async Task<FeeContext> LoadContextAsync(List<int> studentIds, CancellationToken ct)
    {
        var prefix = (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";

        var heads = (await _db.PreSchoolFeeHeads.AsNoTracking().Where(f => f.IsActive)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync(ct))
            .GroupBy(f => f.ClassId).ToDictionary(g => g.Key, g => g.ToList());

        var receipts = studentIds.Count == 0
            ? []
            : await _db.PreSchoolReceipts.AsNoTracking().Include(r => r.Lines).Include(r => r.Payments)
                .Where(r => studentIds.Contains(r.StudentId))
                .OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.ReceiptNo)
                .ToListAsync(ct);

        var live = receipts.Where(r => !r.IsCancelled).ToList();
        var paidDetail = live
            .SelectMany(r => r.Lines.Where(l => l.Kind == KindMonthly && l.ForMonth != null).Select(l => (r.StudentId, Month: l.ForMonth!.Value, l.Amount, r.ReceiptNo)))
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Month).ToDictionary(m => m.Key, m => (m.Sum(x => x.Amount), m.First().ReceiptNo)));

        return new FeeContext(
            prefix,
            heads,
            paidDetail.ToDictionary(kv => kv.Key, kv => kv.Value.Keys.ToHashSet()),
            paidDetail,
            live.Where(r => r.Lines.Any(l => l.Kind == KindAdmission)).Select(r => r.StudentId).ToHashSet(),
            receipts.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    private static decimal MonthlyFee(PreSchoolStudent s, FeeContext ctx)
    {
        var total = (ctx.HeadsByClass.GetValueOrDefault(s.ClassId) ?? []).Sum(h => h.MonthlyAmount);
        return Math.Max(0, total - s.MonthlyDiscount);
    }

    /// <summary>Every month from joining to now (or to the end) that has no receipt; none while the class has no fee.</summary>
    private static List<DateOnly> DueMonths(PreSchoolStudent s, FeeContext ctx, decimal fee)
    {
        if (fee <= 0) return [];
        var paid = ctx.PaidMonths.GetValueOrDefault(s.Id) ?? [];
        return MonthRange(s).Where(m => !paid.Contains(m)).ToList();
    }

    private static IEnumerable<DateOnly> MonthRange(PreSchoolStudent s)
    {
        var start = FirstOfMonth(s.JoinedOn);
        var end = s.Status == PreSchoolStudentStatus.Active
            ? FirstOfMonth(DateOnly.FromDateTime(DateTime.Today))
            : FirstOfMonth(s.EndedOn ?? s.JoinedOn);
        for (var m = start; m <= end; m = m.AddMonths(1)) yield return m;
    }

    private static PreSchoolStudentDetailDto ToDetail(PreSchoolStudent s, FeeContext ctx)
    {
        var fee = MonthlyFee(s, ctx);
        var paid = ctx.PaidDetail.GetValueOrDefault(s.Id) ?? [];

        // The months to show: every month the child is billed for so far, plus
        // any paid in advance beyond it.
        var months = MonthRange(s).Union(paid.Keys).OrderBy(m => m)
            .Select(m => paid.TryGetValue(m, out var p)
                ? new PreSchoolMonthDto(m, true, p.Amount, ReceiptCode(ctx.Prefix, p.ReceiptNo))
                : new PreSchoolMonthDto(m, false, fee, null))
            .ToList();

        var receipts = (ctx.Receipts.GetValueOrDefault(s.Id) ?? [])
            .Select(r => new PreSchoolReceiptSummaryDto(
                r.Id, ReceiptCode(ctx.Prefix, r.ReceiptNo), r.ReceivedOn, r.Total, r.Mode, r.IsCancelled, Summarise(r),
                Paid(r), Balance(r), Payments(r)))
            .ToList();

        return new PreSchoolStudentDetailDto(
            s.Id, AdmissionCode(s, ctx), s.ChildName, s.DateOfBirth, s.Gender, s.BloodGroup, s.FatherName, s.MotherName,
            s.ParentName, s.Phone, s.EmergencyPhone, s.Email, s.Address,
            s.GuardianRelation, s.Guardian2Name, s.Guardian2Relation, s.Guardian2Phone, s.HeightCm, s.WeightKg, s.ClassId, s.Class?.Name ?? "", s.JoinedOn,
            s.MonthlyDiscount, s.Status, s.EndedOn, s.Notes, fee, s.Class?.AdmissionFee ?? 0, ctx.AdmissionPaid.Contains(s.Id),
            months, receipts, s.Section, s.ReferenceType, s.ReferredByStaffId, s.ReferredBy?.Name);
    }

    /// <summary>"Admission + Nov 2026, Dec 2026" — what a receipt paid for, in a line.</summary>
    private static string Summarise(PreSchoolReceipt r)
    {
        var parts = new List<string>();
        if (r.Lines.Any(l => l.Kind == KindAdmission)) parts.Add("Admission");
        parts.AddRange(r.Lines.Where(l => l.ForMonth != null).Select(l => l.ForMonth!.Value).Distinct().OrderBy(m => m).Select(m => m.ToString("MMM yyyy")));
        parts.AddRange(r.Lines.Where(l => l.Kind == KindCharge && l.ChargeHead != null)
            .Select(l => PreSchoolChargeHeads.Labels.GetValueOrDefault(l.ChargeHead!, l.ChargeHead!)));
        if (r.Lines.Any(l => l.Kind == KindDiscount)) parts.Add("less discount");
        return string.Join(", ", parts);
    }

    /// <summary>The next number from a running sequence; two desks saving at once never get the same one.</summary>
    /// <remarks>
    /// A plain command, not SqlQueryRaw: EF wraps a raw query in a sub-select to
    /// apply Single(), and SQL Server refuses NEXT VALUE FOR inside one.
    /// </remarks>
    private async Task<int> NextAsync(string sequence, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT NEXT VALUE FOR dbo.{sequence}";
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    private static string AdmissionCode(PreSchoolStudent s, FeeContext ctx) => AdmissionCode(ctx.Prefix, s.AdmissionNo);
    private static string AdmissionCode(string prefix, int no) => $"{prefix}-{no:0000}";
    private static string ReceiptCode(string prefix, int no) => $"{prefix}/{no:0000}";
    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
