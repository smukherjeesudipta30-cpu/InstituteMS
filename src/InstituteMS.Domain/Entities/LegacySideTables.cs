namespace InstituteMS.Domain.Entities;

/* =====================================================================
   Side tables for dbo.Expense and dbo.OIncome.

   Neither table can gain a column. The legacy application writes both
   with "INSERT INTO t VALUES (...)" and no column list, and such a
   statement must supply a value for every non-identity column - a
   DEFAULT does not excuse it. Adding Branch to dbo.Expense would break
   recording an expense in the legacy application the moment it ran.

   So the fields the new application needs live beside the row, keyed by
   the legacy id, in tables the legacy application does not know about.

   The relationship is OPTIONAL on purpose. When the legacy application
   records an expense, no side row is created, and such an expense must
   still appear in the new application's lists and reports - it does,
   with a null branch, which the branch report buckets as "Unassigned".
   Making the relationship required would turn the query into an INNER
   JOIN and silently hide every expense the legacy application entered.
   Verified against a legacy-shaped database: an expense inserted the way
   the legacy application inserts it is returned by the new application,
   and lands in the Unassigned bucket rather than disappearing.
   ===================================================================== */

/// <summary>
/// The columns <c>dbo.Expense</c> cannot hold. Maps to <c>dbo.ExpenseExt</c>.
/// </summary>
public class ExpenseExt
{
    /// <summary>The <c>dbo.Expense.id</c> this belongs to; also the key.</summary>
    public int ExpenseId { get; set; }

    /// <summary>
    /// Which branch the money was spent at. Free text matching
    /// <c>Student.Branch</c>, because that is how a branch is already recorded.
    /// </summary>
    public string? Branch { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// The columns <c>dbo.OIncome</c> cannot hold. Maps to <c>dbo.OIncomeExt</c>.
/// </summary>
public class OtherIncomeExt
{
    /// <summary>The <c>dbo.OIncome.ID</c> this belongs to; also the key.</summary>
    public int IncomeId { get; set; }

    public string? Branch { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// The columns <c>dbo.CusMessage</c> cannot hold. Maps to <c>dbo.CusMessageExt</c>.
/// </summary>
/// <remarks>
/// The dashboard's pending-enquiries panel needs to know which website enquiries
/// have been dealt with, and <c>dbo.CusMessage</c> is bare-inserted by the legacy
/// application's About page, so it cannot gain an <c>IsHandled</c> column.
///
/// <para>No side row means not handled, which is the right default: an enquiry
/// nobody has touched is exactly the one that should still be showing.</para>
/// </remarks>
public class ContactMessageExt
{
    /// <summary>The <c>dbo.CusMessage.id</c> this belongs to; also the key.</summary>
    public int MessageId { get; set; }

    public bool IsHandled { get; set; }
    public string? HandledBy { get; set; }
    public DateTime? HandledOn { get; set; }
}
