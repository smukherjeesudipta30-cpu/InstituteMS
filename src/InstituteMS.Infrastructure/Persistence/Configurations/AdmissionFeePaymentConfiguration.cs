using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class AdmissionFeePaymentConfiguration : IEntityTypeConfiguration<AdmissionFeePayment>
{
    public void Configure(EntityTypeBuilder<AdmissionFeePayment> b)
    {
        b.ToTable("AdmissionFeePayment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.StudentId).HasColumnName("StudentId");
        b.Property(x => x.RegistrationId).HasColumnName("RegistrationId").HasMaxLength(50);
        b.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2);
        b.Property(x => x.PaymentMode).HasColumnName("PaymentMode").HasMaxLength(10);
        b.Property(x => x.PaidOn).HasColumnName("PaidOn");
        b.Property(x => x.IsAtAdmission).HasColumnName("IsAtAdmission");
        b.Property(x => x.Note).HasColumnName("Note").HasMaxLength(250);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(100);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        // Every read of this table is "the receipts for one student".
        b.HasIndex(x => x.StudentId);
    }
}
