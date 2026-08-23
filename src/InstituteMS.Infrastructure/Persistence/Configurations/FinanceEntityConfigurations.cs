using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class LateFeeConfiguration : IEntityTypeConfiguration<LateFee>
{
    public void Configure(EntityTypeBuilder<LateFee> b)
    {
        b.ToTable("LateFees");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(15);
        b.Property(x => x.BillNo).HasColumnName("BillNo").HasMaxLength(50);
        b.Property(x => x.FineAmount).HasColumnName("FineFees").HasPrecision(18, 2);
        b.Property(x => x.FineDate).HasColumnName("DOD");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => x.StudentRegistrationId);
        b.HasIndex(x => x.FineDate);
    }
}

public class TeacherFeeConfiguration : IEntityTypeConfiguration<TeacherFee>
{
    public void Configure(EntityTypeBuilder<TeacherFee> b)
    {
        b.ToTable("TecFees");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TeacherId).HasColumnName("TecId");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(15);
        b.Property(x => x.BillNo).HasColumnName("BillNo").HasMaxLength(50);
        b.Property(x => x.Salary).HasColumnName("TecSal").HasPrecision(18, 2);
        b.Property(x => x.PaidOn).HasColumnName("UpdateDate");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.TeacherId);
        b.HasIndex(x => x.PaidOn);
    }
}

public class TeacherExpenseConfiguration : IEntityTypeConfiguration<TeacherExpense>
{
    public void Configure(EntityTypeBuilder<TeacherExpense> b)
    {
        b.ToTable("TecExpense");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TeacherId).HasColumnName("TecId");
        b.Property(x => x.ExpenseDate).HasColumnName("InDate");
        b.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Teacher)
            .WithMany()
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ExpenseDate);
    }
}

public class MarkConfiguration : IEntityTypeConfiguration<Mark>
{
    public void Configure(EntityTypeBuilder<Mark> b)
    {
        b.ToTable("Marks");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StudentRegistrationId).HasColumnName("RegId").HasMaxLength(15);
        b.Property(x => x.ExamDate).HasColumnName("ExDate");

        b.Property(x => x.Subject1).HasColumnName("Sub1").HasMaxLength(30);
        b.Property(x => x.FullMarks1).HasColumnName("M1");
        b.Property(x => x.ObtainedMarks1).HasColumnName("OM1");
        b.Property(x => x.Subject2).HasColumnName("Sub2").HasMaxLength(30);
        b.Property(x => x.FullMarks2).HasColumnName("M2");
        b.Property(x => x.ObtainedMarks2).HasColumnName("OM2");
        b.Property(x => x.Subject3).HasColumnName("Sub3").HasMaxLength(30);
        b.Property(x => x.FullMarks3).HasColumnName("M3");
        b.Property(x => x.ObtainedMarks3).HasColumnName("OM3");
        b.Property(x => x.Subject4).HasColumnName("Sub4").HasMaxLength(30);
        b.Property(x => x.FullMarks4).HasColumnName("M4");
        b.Property(x => x.ObtainedMarks4).HasColumnName("OM4");

        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.StudentRegistrationId);
    }
}

public class FeeTotalConfiguration : IEntityTypeConfiguration<FeeTotal>
{
    public void Configure(EntityTypeBuilder<FeeTotal> b)
    {
        b.ToTable("Fees");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuReg").HasMaxLength(15);
        b.Property(x => x.Total).HasColumnName("Total").HasPrecision(18, 2);
        b.Property(x => x.StoreDate).HasColumnName("StoreDate");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => x.StudentRegistrationId);
        b.HasIndex(x => x.StoreDate);
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branch");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("BranchName").HasMaxLength(50);
        b.Property(x => x.Code).HasColumnName("BranchCode").HasMaxLength(50);
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
    }
}
