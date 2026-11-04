using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class PreSchoolClassConfiguration : IEntityTypeConfiguration<PreSchoolClass>
{
    public void Configure(EntityTypeBuilder<PreSchoolClass> b)
    {
        b.ToTable("PreSchoolClass");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.AdmissionFee).HasPrecision(10, 2);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class PreSchoolSubjectConfiguration : IEntityTypeConfiguration<PreSchoolSubject>
{
    public void Configure(EntityTypeBuilder<PreSchoolSubject> b)
    {
        b.ToTable("PreSchoolSubject");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.ClassId, x.Name }).IsUnique();
        b.HasOne(x => x.Class).WithMany(c => c.Subjects).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolFeeHeadConfiguration : IEntityTypeConfiguration<PreSchoolFeeHead>
{
    public void Configure(EntityTypeBuilder<PreSchoolFeeHead> b)
    {
        b.ToTable("PreSchoolFeeHead");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.MonthlyAmount).HasPrecision(10, 2);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.ClassId, x.Name }).IsUnique();
        b.HasOne(x => x.Class).WithMany(c => c.FeeHeads).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolGradeBandConfiguration : IEntityTypeConfiguration<PreSchoolGradeBand>
{
    public void Configure(EntityTypeBuilder<PreSchoolGradeBand> b)
    {
        b.ToTable("PreSchoolGradeBand");
        b.HasKey(x => x.Id);
        b.Property(x => x.MinPercent).HasPrecision(5, 2);
        b.Property(x => x.Grade).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remark).HasMaxLength(80);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.Grade).IsUnique();
    }
}

public class PreSchoolSettingConfiguration : IEntityTypeConfiguration<PreSchoolSetting>
{
    public void Configure(EntityTypeBuilder<PreSchoolSetting> b)
    {
        b.ToTable("PreSchoolSetting");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SchoolName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.MapUrl).HasMaxLength(500);
        b.Property(x => x.UniformShopName).HasMaxLength(150);
        b.Property(x => x.UniformShopAddress).HasMaxLength(400);
        b.Property(x => x.UniformShopPhone).HasMaxLength(50);
        b.Property(x => x.UniformShopMapUrl).HasMaxLength(500);
        b.Property(x => x.ReceiptPrefix).HasMaxLength(10).IsRequired();
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}

public class PreSchoolEnquiryConfiguration : IEntityTypeConfiguration<PreSchoolEnquiry>
{
    public void Configure(EntityTypeBuilder<PreSchoolEnquiry> b)
    {
        b.ToTable("PreSchoolEnquiry");
        b.HasKey(x => x.Id);
        b.Property(x => x.ChildName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.ClassWanted).HasMaxLength(50);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.ClientIp).HasMaxLength(50);
        b.Property(x => x.HandledBy).HasMaxLength(250);
        b.HasIndex(x => x.CreatedOn);
    }
}
