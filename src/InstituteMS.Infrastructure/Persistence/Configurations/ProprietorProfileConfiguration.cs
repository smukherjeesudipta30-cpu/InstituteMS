using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ProprietorProfileConfiguration : IEntityTypeConfiguration<ProprietorProfile>
{
    public void Configure(EntityTypeBuilder<ProprietorProfile> b)
    {
        b.ToTable("ProprietorProfile");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.ModifiedBy).HasMaxLength(250).IsUnicode(false);
    }
}
