namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Expense</c> — institute running costs.</summary>
public class Expense
{
    public int Id { get; set; }
    public string? Details { get; set; }
    public DateOnly? ExpenseDate { get; set; }
    public decimal? Amount { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
