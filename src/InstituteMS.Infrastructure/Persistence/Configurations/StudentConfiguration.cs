using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("Student");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("StuName").HasMaxLength(100);
        b.Property(x => x.GuardianName).HasColumnName("GName").HasMaxLength(300);
        b.Property(x => x.Address).HasColumnName("Stuaddress");
        b.Property(x => x.Phone).HasColumnName("StuPhone").HasMaxLength(10);
        b.Property(x => x.Email).HasColumnName("StuEmail").HasMaxLength(50);
        b.Property(x => x.RegistrationId).HasColumnName("StuRegId").HasMaxLength(15);
        b.Property(x => x.BillNo).HasColumnName("BillNo").HasMaxLength(50);
        b.Property(x => x.TechniqueCount).HasColumnName("NoTech");
        b.Property(x => x.AdmissionFees).HasColumnName("StuAdFees").HasPrecision(18, 2);
        b.Property(x => x.AdmissionPaymentMode).HasColumnName("AdmissionPaymentMode").HasMaxLength(10);
        b.Property(x => x.AdmissionFeesPaid).HasColumnName("AdmissionFeesPaid").HasPrecision(18, 2);
        b.Property(x => x.ReferrerType).HasColumnName("ReferrerType").HasMaxLength(15);
        b.Property(x => x.ReferrerId).HasColumnName("ReferrerId");
        b.Property(x => x.ReferralPercent).HasColumnName("ReferralPercent").HasPrecision(5, 2);
        b.Property(x => x.ReferrerSummary).HasColumnName("ReferrerSummary").HasMaxLength(500);
        b.Property(x => x.ExamFeeOverride).HasColumnName("ExamFeeOverride").HasPrecision(12, 2);
        // Stu_monthly_fees is varchar(250) in the legacy schema, not a number.
        b.Property(x => x.MonthlyFees).HasColumnName("Stu_monthly_fees")
            .HasConversion<DecimalOverTextConverter>().HasMaxLength(250);
        b.Property(x => x.DateOfJoining).HasColumnName("DOJ");
        b.Property(x => x.DateOfBirth).HasColumnName("DOB");
        b.Property(x => x.DateOfExit).HasColumnName("DOE");
        b.Property(x => x.CourseId).HasColumnName("Cid");
        b.Property(x => x.PrimaryTeacherId).HasColumnName("TecName");
        b.Property(x => x.SecondTeacherId).HasColumnName("TecName1");
        b.Property(x => x.ThirdTeacherId).HasColumnName("TecName2");
        // Both are NULLable bit in the legacy schema, and the properties are
        // nullable to match - see Student.IsActive.
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.IsComplete).HasColumnName("IsComplete");
        b.Property(x => x.LoginId).HasColumnName("loginId").HasMaxLength(100);
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(50);
        b.Property(x => x.PersonalEmail).HasColumnName("Personal_mail_id").HasMaxLength(150);
        b.Property(x => x.AvijanLoginEmail).HasColumnName("Avijan_login_mail_id").HasMaxLength(150);
        b.Property(x => x.AvijanLoginPassword).HasColumnName("Avijan_login_Password").HasMaxLength(250);
        b.Property(x => x.ReplicaId).HasColumnName("Replica_id");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        // Deliberately NOT unique: the legacy data contains 12 registration ids
        // shared by more than one student, caused by the unlocked
        // "SELECT TOP 1 id ... + 1" generator (defect D4). See db/migrations/003.
        b.HasIndex(x => x.RegistrationId);

        b.HasOne(x => x.Course)
            .WithMany(c => c.Students)
            .HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Three separate teacher FKs — Restrict avoids SQL Server's
        // multiple-cascade-path error (MIGRATION_PLAN §3.2.3).
        b.HasOne(x => x.PrimaryTeacher)
            .WithMany()
            .HasForeignKey(x => x.PrimaryTeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SecondTeacher)
            .WithMany()
            .HasForeignKey(x => x.SecondTeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ThirdTeacher)
            .WithMany()
            .HasForeignKey(x => x.ThirdTeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
