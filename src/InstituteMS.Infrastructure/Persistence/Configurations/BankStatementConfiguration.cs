using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
{
    public void Configure(EntityTypeBuilder<BankStatement> b)
    {
        b.ToTable("BankStatement");
        b.HasKey(x => x.MonthStart);
        b.Property(x => x.DataJson).IsRequired();
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}
