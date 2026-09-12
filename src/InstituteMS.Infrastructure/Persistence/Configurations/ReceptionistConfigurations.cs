using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ReceptionistConfiguration : IEntityTypeConfiguration<Receptionist>
{
    public void Configure(EntityTypeBuilder<Receptionist> b)
    {
        b.ToTable("Receptionist");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("RecName").HasMaxLength(100).IsRequired();
        b.Property(x => x.LoginId).HasColumnName("LoginId").HasMaxLength(100);
        b.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
        b.Property(x => x.PersonalEmail).HasColumnName("PersonalEmail").HasMaxLength(150);
        b.Property(x => x.Designation).HasColumnName("Designation").HasMaxLength(150);
        b.Property(x => x.Qualification).HasColumnName("Qualification").HasMaxLength(200);
        b.Property(x => x.ComputerTraining).HasColumnName("ComputerTraining").HasMaxLength(200);
        b.Property(x => x.IsTechnical).HasColumnName("IsTechnical");
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasMany(x => x.Payments)
            .WithOne(p => p.Receptionist)
            .HasForeignKey(p => p.ReceptionistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReceptionistPaymentConfiguration : IEntityTypeConfiguration<ReceptionistPayment>
{
    public void Configure(EntityTypeBuilder<ReceptionistPayment> b)
    {
        b.ToTable("ReceptionistPayment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ReceptionistId).HasColumnName("ReceptionistId");
        b.Property(x => x.PayMonth).HasColumnName("PayMonth");
        b.Property(x => x.PaidOn).HasColumnName("PaidOn");
        b.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2);
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.Notes).HasColumnName("Notes").HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        // One payment per person per month; a second is a correction, not a
        // second salary.
        b.HasIndex(x => new { x.ReceptionistId, x.PayMonth }).IsUnique();
    }
}
