namespace InstituteMS.Domain.Entities;

/// <summary>
/// The proprietor's monthly honorarium, set by Admin. Institute-wide (never a
/// branch's cost). Each row applies from its month until the next row, so a
/// change never rewrites the profit of months already reported.
/// </summary>
public class ProprietorHonorarium
{
    public int Id { get; set; }

    /// <summary>First day of the month this amount starts from.</summary>
    public DateOnly EffectiveFrom { get; set; }

    public decimal MonthlyAmount { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
