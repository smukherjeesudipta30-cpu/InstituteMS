using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ProprietorHonorariumConfiguration : IEntityTypeConfiguration<ProprietorHonorarium>
{
    public void Configure(EntityTypeBuilder<ProprietorHonorarium> b)
    {
        b.ToTable("ProprietorHonorarium");
        b.HasKey(x => x.Id);
        b.Property(x => x.MonthlyAmount).HasPrecision(12, 2);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.Branch).HasMaxLength(100);
        b.HasIndex(x => new { x.Branch, x.EffectiveFrom }).IsUnique();
    }
}
