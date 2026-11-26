using FluentValidation;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.PreSchool;

public record PreSchoolStaffDto(
    int Id,
    string Name,
    string Role,
    string? Phone,
    string? Email,
    DateOnly JoinedOn,
    DateOnly? LeftOn,
    decimal MonthlySalary,
    string? Notes,
    bool IsWorking);

public record PreSchoolStaffSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = PreSchoolStaffRoles.Teacher;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public DateOnly JoinedOn { get; init; }
    public DateOnly? LeftOn { get; init; }
    public decimal MonthlySalary { get; init; }
    public string? Notes { get; init; }
}

public class PreSchoolStaffSaveRequestValidator : AbstractValidator<PreSchoolStaffSaveRequest>
{
    public PreSchoolStaffSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the name.").MaximumLength(100);
        RuleFor(x => x.Role).Must(r => PreSchoolStaffRoles.All.Contains(r))
            .WithMessage("Role is Receptionist, Teacher or Housekeeper.");
        RuleFor(x => x.Phone).Matches(@"^\+?[0-9 \-]{10,15}$").When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a 10-digit phone number, or leave it empty.");
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address, or leave it empty.");
        RuleFor(x => x.JoinedOn).NotEqual(default(DateOnly)).WithMessage("Pick the joining date.");
        RuleFor(x => x).Must(x => x.LeftOn is null || x.LeftOn >= x.JoinedOn)
            .WithMessage("The leaving date cannot be before the joining date.");
        RuleFor(x => x.MonthlySalary).GreaterThan(0).WithMessage("Enter the monthly salary.");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public record PreSchoolSalaryPaymentDto(
    int Id,
    int StaffId,
    string StaffName,
    string Role,
    DateOnly ForMonth,
    DateOnly PaidOn,
    decimal Salary,
    decimal DaysAbsent,
    decimal Deduction,
    decimal NetPaid,
    string NetPaidInWords,
    string Mode,
    string? Reference,
    string? Notes,
    string? PaidBy,
    string SchoolName,
    string? SchoolAddress,
    // Reference fees paid with this salary (included in NetPaid).
    decimal ReferralBonus = 0);

/// <summary>One reference fee: the referrer's share of one payment on a child's admission receipt.</summary>
public record PreSchoolReferralCreditDto(
    int Id, DateOnly CreditedOn, string ChildName, string AdmissionCode, string ReceiptCode, int InstalmentNo,
    decimal PaymentAmount, decimal Percent, decimal Amount, string StaffName, DateOnly? PaidWithSalaryFor);

/// <summary>One person's honorarium over the report's dates.</summary>
public record PreSchoolHonorariumStaffDto(
    int StaffId, string Name, string Role, int Payments, decimal Honorarium, decimal Deduction, decimal ReferenceFees, decimal NetPaid,
    // Reference fees earned in the dates (paid or still to come).
    decimal ReferenceEarned);

/// <summary>
/// Honorarium paid to teachers, receptionists and housekeepers. By month: the
/// honorarium for each month from the 1st to the last day of From..To's months.
/// By dates: every payment made between From and To.
/// </summary>
public record PreSchoolHonorariumReportDto(
    DateOnly From,
    DateOnly To,
    string Basis,
    decimal Honorarium,
    decimal Deduction,
    decimal ReferenceFees,
    decimal NetPaid,
    IReadOnlyList<PreSchoolHonorariumStaffDto> ByStaff,
    IReadOnlyList<PreSchoolSalaryPaymentDto> Payments,
    IReadOnlyList<PreSchoolReferralCreditDto> ReferenceCredits);

/// <summary>A teacher's or receptionist's reference fees: those still to be paid and those paid with a salary.</summary>
public record PreSchoolMyReferralsDto(decimal Pending, decimal PaidOut, IReadOnlyList<PreSchoolReferralCreditDto> Credits);

/// <summary>Every payment received between two dates, with who collected it, and the totals by mode, collector, class and day.</summary>
public record PreSchoolCollectionReportDto(
    DateOnly From,
    DateOnly To,
    decimal Total,
    int Payments,
    IReadOnlyList<PreSchoolIncomeReceiptLineDto> Rows,
    IReadOnlyDictionary<string, decimal> ByMode,
    IReadOnlyDictionary<string, decimal> ByCollector,
    IReadOnlyDictionary<string, decimal> ByClass,
    IReadOnlyDictionary<DateOnly, decimal> ByDay,
    decimal AdmissionFees,
    decimal MonthlyFees,
    decimal OtherFees,
    decimal Discounts);

/// <summary>One person in a month's salary sheet; <see cref="Payment"/> is null until they are paid.</summary>
public record PreSchoolSalaryRowDto(int StaffId, string Name, string Role, decimal MonthlySalary, PreSchoolSalaryPaymentDto? Payment,
    // Reference fees earned up to the end of the month and not yet paid: added when the salary is paid.
    decimal PendingReferral = 0);

public record PreSchoolSalarySheetDto(DateOnly Month, IReadOnlyList<PreSchoolSalaryRowDto> Rows, decimal TotalSalary, decimal TotalPaid, decimal TotalUnpaid);

public record PreSchoolSalaryPayRequest
{
    public int StaffId { get; init; }
    public DateOnly ForMonth { get; init; }
    public DateOnly? PaidOn { get; init; }
    public decimal DaysAbsent { get; init; }
    public decimal Deduction { get; init; }
    public string Mode { get; init; } = "Cash";
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public class PreSchoolSalaryPayRequestValidator : AbstractValidator<PreSchoolSalaryPayRequest>
{
    public PreSchoolSalaryPayRequestValidator()
    {
        RuleFor(x => x.StaffId).GreaterThan(0);
        RuleFor(x => x.ForMonth).NotEqual(default(DateOnly)).WithMessage("Pick the month.");
        RuleFor(x => x.DaysAbsent).InclusiveBetween(0, 31).WithMessage("Days absent must be between 0 and 31.");
        RuleFor(x => x.Deduction).GreaterThanOrEqualTo(0).WithMessage("The deduction cannot be negative.");
        RuleFor(x => x.Mode).Must(m => m is "Cash" or "Bank" or "UPI").WithMessage("Payment mode is Cash, Bank or UPI.");
        RuleFor(x => x.Reference).MaximumLength(60);
        RuleFor(x => x.PaidOn).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.PaidOn.HasValue).WithMessage("The payment date cannot be in the future.");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public record PreSchoolExpenseDto(int Id, DateOnly SpentOn, string Head, string? Description, decimal Amount, string Mode, string? Reference, string? CreatedBy);

public record PreSchoolExpenseSaveRequest
{
    public DateOnly SpentOn { get; init; }
    public string Head { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Amount { get; init; }
    public string Mode { get; init; } = "Cash";
    public string? Reference { get; init; }
}

public class PreSchoolExpenseSaveRequestValidator : AbstractValidator<PreSchoolExpenseSaveRequest>
{
    public PreSchoolExpenseSaveRequestValidator()
    {
        RuleFor(x => x.SpentOn).NotEqual(default(DateOnly)).WithMessage("Pick the date.")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today)).WithMessage("The date cannot be in the future.");
        RuleFor(x => x.Head).NotEmpty().WithMessage("Pick what the money was spent on.").MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter the amount.");
        RuleFor(x => x.Mode).Must(m => m is "Cash" or "Bank" or "UPI").WithMessage("Payment mode is Cash, Bank or UPI.");
        RuleFor(x => x.Reference).MaximumLength(60);
    }
}

public record PreSchoolIncomeMonthDto(
    DateOnly Month,
    decimal AdmissionFees,
    decimal MonthlyFees,
    decimal Income,
    decimal Salaries,
    decimal Expenses,
    decimal Net,
    // Registration, annual, kit, food, transport and the receipt's other rows.
    decimal OtherFees = 0,
    // Paid to My Chhota School for store stock, by the day paid (bulk payments, not deliveries).
    decimal Purchases = 0,
    // Discounts and special offers given on receipts (already taken off Income).
    decimal Discounts = 0,
    // Reference fees paid to teachers and receptionists with their salaries (not in Salaries).
    decimal Referrals = 0);

/// <summary>
/// A year of the PreSchool's money, month by month: fees received (by receipt
/// date), salaries paid (by payment date) and other expenses (by spending
/// date). Cancelled receipts count for nothing.
/// </summary>
public record PreSchoolIncomeReportDto(
    int Year,
    IReadOnlyList<PreSchoolIncomeMonthDto> Months,
    decimal Income,
    decimal Salaries,
    decimal Expenses,
    decimal Net,
    IReadOnlyDictionary<string, decimal> IncomeByMode,
    IReadOnlyDictionary<string, decimal> ExpensesByHead,
    decimal Purchases = 0,
    // Fees received, by the login that collected them.
    IReadOnlyDictionary<string, decimal>? IncomeByCollector = null,
    // Reference fees paid to teachers and receptionists with their salaries (not in Salaries).
    decimal Referrals = 0);

/// <summary>One delivery of store stock, with what it cost.</summary>
public record PreSchoolIncomePurchaseLineDto(DateOnly OnDate, string ItemName, string ClassName, string Supplier, int Quantity, decimal UnitCost, decimal Amount);

/// <summary>
/// One kind of store item over the dates: what parents paid for it on their
/// receipts against what was spent buying it, and how many were bought and given.
/// </summary>
public record PreSchoolStoreMarginDto(string Item, string? ChargeHead, decimal ReceivedFromParents, decimal Spent, decimal Margin, int Bought, int Given);

/// <summary>
/// One payment received (the whole receipt, or one instalment of it). The fee
/// columns are this payment's share of the receipt's rows.
/// </summary>
public record PreSchoolIncomeReceiptLineDto(DateOnly ReceivedOn, string ReceiptCode, string ChildName, string ClassName, string Mode, decimal AdmissionFee, decimal MonthlyFees, decimal Total, decimal OtherFees = 0, decimal Discount = 0,
    string? CollectedBy = null, int InstalmentNo = 1, decimal BillTotal = 0,
    // The child's admission details, for the fee collection report.
    string? AdmissionCode = null, string? ParentName = null, string? Phone = null, DateOnly? JoinedOn = null, string? Section = null,
    string? Reference = null, int StudentId = 0);
public record PreSchoolIncomeSalaryLineDto(DateOnly PaidOn, string StaffName, string Role, DateOnly ForMonth, string Mode, decimal Amount,
    // Of Amount, the reference fees paid with this salary.
    decimal ReferralBonus = 0);

/// <summary>
/// The PreSchool's money between any two dates, with every receipt, salary and
/// expense behind the totals, for downloading. Cancelled receipts count for nothing.
/// </summary>
public record PreSchoolIncomeRangeDto(
    DateOnly From,
    DateOnly To,
    decimal AdmissionFees,
    decimal MonthlyFees,
    decimal Income,
    decimal Salaries,
    decimal Expenses,
    decimal Net,
    IReadOnlyDictionary<string, decimal> IncomeByMode,
    IReadOnlyDictionary<string, decimal> ExpensesByHead,
    IReadOnlyList<PreSchoolIncomeReceiptLineDto> Receipts,
    IReadOnlyList<PreSchoolIncomeSalaryLineDto> SalaryPayments,
    IReadOnlyList<PreSchoolExpenseDto> ExpenseEntries,
    decimal OtherFees = 0,
    decimal Purchases = 0,
    IReadOnlyList<PreSchoolIncomePurchaseLineDto>? PurchaseEntries = null,
    IReadOnlyList<PreSchoolStoreMarginDto>? StoreMargins = null,
    // Payments to My Chhota School in these dates (counted in Purchases), and what was still owed on the last day.
    IReadOnlyList<PreSchoolSupplierPaymentDto>? SupplierPayments = null,
    decimal OwedToSupplier = 0,
    // Stock received in these dates at its price (owed, not yet a cost until paid).
    decimal StockReceived = 0,
    // Discounts and special offers given on receipts in these dates (already taken off Income).
    decimal Discounts = 0,
    // Fees received, by the login that collected them.
    IReadOnlyDictionary<string, decimal>? IncomeByCollector = null,
    // Reference fees paid to teachers and receptionists with their salaries (not in Salaries).
    decimal Referrals = 0);

/// <summary>The PreSchool's staff and salaries, expenses and income report. Nothing here reads a Computer Centre table.</summary>
public interface IPreSchoolFinanceService
{
    Task<IReadOnlyList<PreSchoolStaffDto>> ListStaffAsync(CancellationToken ct = default);
    Task<Result<PreSchoolStaffDto>> SaveStaffAsync(int? id, PreSchoolStaffSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Everyone working in a month, with what they were paid for it.</summary>
    Task<PreSchoolSalarySheetDto> SalarySheetAsync(DateOnly month, CancellationToken ct = default);
    Task<Result<PreSchoolSalaryPaymentDto>> PaySalaryAsync(PreSchoolSalaryPayRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolSalaryPaymentDto>> GetSalaryPaymentAsync(int id, CancellationToken ct = default);

    /// <summary>Takes back a salary entered in error, so the month can be paid again correctly.</summary>
    Task<Result<bool>> DeleteSalaryPaymentAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PreSchoolExpenseDto>> ListExpensesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<Result<PreSchoolExpenseDto>> SaveExpenseAsync(int? id, PreSchoolExpenseSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<bool>> DeleteExpenseAsync(int id, CancellationToken ct = default);

    Task<PreSchoolIncomeReportDto> IncomeAsync(int year, CancellationToken ct = default);

    /// <summary>Income, salaries and expenses between two dates, with every entry behind them.</summary>
    Task<Result<PreSchoolIncomeRangeDto>> IncomeRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The signed-in staff member's own salary payments, newest first. They are
    /// found by the email on their staff record matching their sign-in; none
    /// when the admin has not entered it.
    /// </summary>
    Task<IReadOnlyList<PreSchoolSalaryPaymentDto>> MyPayslipsAsync(string email, CancellationToken ct = default);

    /// <summary>The signed-in teacher's or receptionist's reference fees, paid and still to come.</summary>
    Task<PreSchoolMyReferralsDto> MyReferralsAsync(string email, CancellationToken ct = default);

    /// <summary>Honorarium paid, for whole months ("month") or by the date paid ("paid").</summary>
    Task<Result<PreSchoolHonorariumReportDto>> HonorariumReportAsync(DateOnly from, DateOnly to, string basis, CancellationToken ct = default);

    /// <summary>Every payment received between two dates, with who collected it.</summary>
    Task<Result<PreSchoolCollectionReportDto>> CollectionReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
