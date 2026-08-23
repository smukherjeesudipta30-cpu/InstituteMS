using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class MonthlyFeeConfiguration : IEntityTypeConfiguration<MonthlyFee>
{
    public void Configure(EntityTypeBuilder<MonthlyFee> b)
    {
        b.ToTable("MonthlyFees");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(15);
        b.Property(x => x.BillNo).HasColumnName("BillNo").HasMaxLength(50);
        b.Property(x => x.FeeDate).HasColumnName("Date1");
        b.Property(x => x.AgreedMonthlyFee).HasColumnName("stu_actual_monthly_fees").HasPrecision(18, 2);
        b.Property(x => x.PaymentDate).HasColumnName("PDate");
        b.Property(x => x.Installment).HasColumnName("Installment").HasPrecision(18, 2);
        b.Property(x => x.OtherInstallment).HasColumnName("OInstallment").HasPrecision(18, 2);
        b.Property(x => x.Percentage).HasColumnName("percen").HasPrecision(18, 2);
        b.Property(x => x.OldDue).HasColumnName("OldDue").HasPrecision(18, 2);
        b.Property(x => x.NewDue).HasColumnName("NewDue").HasPrecision(18, 2);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.StudentRegistrationId);
        b.HasIndex(x => x.FeeDate);
    }
}
