namespace InstituteMS.Domain.Entities;

/// <summary>
/// One month's stock and book-debt statement for the bank (Bank of Maharashtra,
/// Appendix 6). The lines are kept as JSON because the bank's format, not this
/// app, defines them, and they are only ever read and written as a whole month.
/// </summary>
public class BankStatement
{
    /// <summary>First day of the month the statement is for.</summary>
    public DateOnly MonthStart { get; set; }
    public string DataJson { get; set; } = "{}";
    public string? ModifiedBy { get; set; }
    public DateTime ModifiedOn { get; set; }
}
