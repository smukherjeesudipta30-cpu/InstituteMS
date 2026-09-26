using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ExamFeeRateConfiguration : IEntityTypeConfiguration<ExamFeeRate>
{
    public void Configure(EntityTypeBuilder<ExamFeeRate> b)
    {
        b.ToTable("ExamFeeRate");
        b.HasKey(x => x.Id);
        b.Property(x => x.Branch).HasMaxLength(100).IsRequired();
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.CourseId, x.Branch }).IsUnique();
    }
}

public class ExamFeePaymentConfiguration : IEntityTypeConfiguration<ExamFeePayment>
{
    public void Configure(EntityTypeBuilder<ExamFeePayment> b)
    {
        b.ToTable("ExamFeePayment");
        b.HasKey(x => x.Id);
        b.Property(x => x.RegistrationId).HasMaxLength(50);
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.PaymentMode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.HasIndex(x => x.StudentId);
    }
}
