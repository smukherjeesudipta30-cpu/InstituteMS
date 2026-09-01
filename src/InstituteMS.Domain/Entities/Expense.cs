namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Expense</c> — institute running costs.</summary>
/// <remarks>
/// Branch and the audit fields are NOT here. <c>dbo.Expense</c> is one of the
/// seven tables the legacy application writes with a column-list-free INSERT,
/// so it cannot gain a column without breaking that statement. Those fields
/// live on <see cref="ExpenseExt"/> instead, reached through <see cref="Ext"/>.
/// </remarks>
public class Expense
{
    public int Id { get; set; }
    public string? Details { get; set; }
    public DateOnly? ExpenseDate { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>
    /// The side row carrying branch and audit. Null for every expense the
    /// legacy application recorded, so always read it defensively.
    /// </summary>
    public ExpenseExt? Ext { get; set; }
}
