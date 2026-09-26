using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ReferralSlabConfiguration : IEntityTypeConfiguration<ReferralSlab>
{
    public void Configure(EntityTypeBuilder<ReferralSlab> b)
    {
        b.ToTable("ReferralSlab");
        b.HasKey(x => x.Id);
        b.Property(x => x.MinFee).HasPrecision(12, 2);
        b.Property(x => x.Percent).HasColumnName("Percent").HasPrecision(5, 2);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}

public class StudentReferrerConfiguration : IEntityTypeConfiguration<StudentReferrer>
{
    public void Configure(EntityTypeBuilder<StudentReferrer> b)
    {
        b.ToTable("StudentReferrer");
        b.HasKey(x => x.Id);
        b.Property(x => x.ReferrerType).HasMaxLength(15).IsRequired();
        b.HasIndex(x => new { x.StudentId, x.ReferrerType, x.ReferrerId }).IsUnique();
    }
}

public class ReferralCreditConfiguration : IEntityTypeConfiguration<ReferralCredit>
{
    public void Configure(EntityTypeBuilder<ReferralCredit> b)
    {
        b.ToTable("ReferralCredit");
        b.HasKey(x => x.Id);
        b.Property(x => x.RegistrationId).HasMaxLength(50);
        b.Property(x => x.ReferrerType).HasMaxLength(15).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.PaymentAmount).HasPrecision(12, 2);
        b.Property(x => x.Percent).HasColumnName("Percent").HasPrecision(5, 2);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.AdmissionFeePaymentId, x.ReferrerType, x.ReferrerId }).IsUnique();
    }
}
