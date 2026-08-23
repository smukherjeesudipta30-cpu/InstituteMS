namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.OIncome</c> — income that is not student fees.</summary>
public class OtherIncome
{
    public int Id { get; set; }
    public string? Purpose { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? IncomeDate { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
