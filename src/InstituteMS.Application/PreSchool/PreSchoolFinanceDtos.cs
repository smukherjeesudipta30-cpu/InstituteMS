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
    string? SchoolAddress);

/// <summary>One person in a month's salary sheet; <see cref="Payment"/> is null until they are paid.</summary>
public record PreSchoolSalaryRowDto(int StaffId, string Name, string Role, decimal MonthlySalary, PreSchoolSalaryPaymentDto? Payment);

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
    decimal Net);

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
    IReadOnlyDictionary<string, decimal> ExpensesByHead);

public record PreSchoolIncomeReceiptLineDto(DateOnly ReceivedOn, string ReceiptCode, string ChildName, string ClassName, string Mode, decimal AdmissionFee, decimal MonthlyFees, decimal Total);
public record PreSchoolIncomeSalaryLineDto(DateOnly PaidOn, string StaffName, string Role, DateOnly ForMonth, string Mode, decimal Amount);

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
    IReadOnlyList<PreSchoolExpenseDto> ExpenseEntries);

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
}
