using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class CertificateSettingConfiguration : IEntityTypeConfiguration<CertificateSetting>
{
    public void Configure(EntityTypeBuilder<CertificateSetting> b)
    {
        b.ToTable("CertificateSetting");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SignatoryName).HasMaxLength(100).IsRequired();
        b.Property(x => x.SignatoryTitle).HasMaxLength(100).IsRequired();
        b.Property(x => x.SignatureContentType).HasMaxLength(20);
        b.Property(x => x.StampContentType).HasMaxLength(20);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}
