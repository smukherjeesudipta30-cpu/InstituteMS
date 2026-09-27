namespace InstituteMS.Domain.Entities;

/// <summary>
/// The proprietor's monthly honorarium, set by Admin — either charged to one
/// branch or, with no branch, to the institute as a whole. Each row applies
/// from its month until the next row for the same branch, so a change never
/// rewrites the profit of months already reported.
/// </summary>
public class ProprietorHonorarium
{
    public int Id { get; set; }

    /// <summary>The branch this amount is charged to (by name, as elsewhere in the income report); null for the whole institute.</summary>
    public string? Branch { get; set; }

    /// <summary>First day of the month this amount starts from.</summary>
    public DateOnly EffectiveFrom { get; set; }

    public decimal MonthlyAmount { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
