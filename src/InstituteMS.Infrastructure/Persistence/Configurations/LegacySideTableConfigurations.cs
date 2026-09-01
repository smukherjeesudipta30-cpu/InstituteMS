using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>dbo.ExpenseExt</c> — the branch and audit fields <c>dbo.Expense</c> cannot
/// hold, keyed by the legacy expense id.
/// </summary>
/// <remarks>
/// Created by <c>db/deploy/LEGACY_COEXIST_UPGRADE.sql</c>. The legacy
/// application knows nothing about this table and never writes to it.
/// </remarks>
public class ExpenseExtConfiguration : IEntityTypeConfiguration<ExpenseExt>
{
    public void Configure(EntityTypeBuilder<ExpenseExt> b)
    {
        b.ToTable("ExpenseExt");
        b.HasKey(x => x.ExpenseId);

        /* Not an identity column: the value is the dbo.Expense.id it belongs
           to, supplied by the application rather than generated here. */
        b.Property(x => x.ExpenseId).HasColumnName("ExpenseId").ValueGeneratedNever();

        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.Branch);
    }
}

/// <inheritdoc cref="ExpenseExtConfiguration"/>
public class OtherIncomeExtConfiguration : IEntityTypeConfiguration<OtherIncomeExt>
{
    public void Configure(EntityTypeBuilder<OtherIncomeExt> b)
    {
        b.ToTable("OIncomeExt");
        b.HasKey(x => x.IncomeId);

        b.Property(x => x.IncomeId).HasColumnName("IncomeId").ValueGeneratedNever();

        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.Branch);
    }
}

/// <inheritdoc cref="ExpenseExtConfiguration"/>
public class ContactMessageExtConfiguration : IEntityTypeConfiguration<ContactMessageExt>
{
    public void Configure(EntityTypeBuilder<ContactMessageExt> b)
    {
        b.ToTable("CusMessageExt");
        b.HasKey(x => x.MessageId);

        b.Property(x => x.MessageId).HasColumnName("MessageId").ValueGeneratedNever();
        b.Property(x => x.IsHandled).HasColumnName("IsHandled");
        b.Property(x => x.HandledBy).HasColumnName("HandledBy").HasMaxLength(50);
        b.Property(x => x.HandledOn).HasColumnName("HandledOn");

        b.HasIndex(x => x.IsHandled);
    }
}
