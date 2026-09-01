namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.OIncome</c> — income that is not student fees.</summary>
/// <remarks>
/// Branch and the audit fields are NOT here, for the same reason they are absent
/// from <see cref="Expense"/>: <c>dbo.OIncome</c> is written by a column-list-free
/// INSERT in the legacy application and cannot gain a column. They live on
/// <see cref="OtherIncomeExt"/>, reached through <see cref="Ext"/>.
/// </remarks>
public class OtherIncome
{
    public int Id { get; set; }
    public string? Purpose { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? IncomeDate { get; set; }

    /// <summary>
    /// The side row carrying branch and audit. Null for every entry the legacy
    /// application recorded, so always read it defensively.
    /// </summary>
    public OtherIncomeExt? Ext { get; set; }
}
