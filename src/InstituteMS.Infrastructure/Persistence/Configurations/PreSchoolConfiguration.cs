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
        b.Property(x => x.OfficePhone).HasMaxLength(50);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
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

public class PreSchoolHolidayConfiguration : IEntityTypeConfiguration<PreSchoolHoliday>
{
    public void Configure(EntityTypeBuilder<PreSchoolHoliday> b)
    {
        b.ToTable("PreSchoolHoliday");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.FromDate);
    }
}

public class PreSchoolExamConfiguration : IEntityTypeConfiguration<PreSchoolExam>
{
    public void Configure(EntityTypeBuilder<PreSchoolExam> b)
    {
        b.ToTable("PreSchoolExam");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class PreSchoolMarkConfiguration : IEntityTypeConfiguration<PreSchoolMark>
{
    public void Configure(EntityTypeBuilder<PreSchoolMark> b)
    {
        b.ToTable("PreSchoolMark");
        b.HasKey(x => x.Id);
        b.Property(x => x.Marks).HasPrecision(6, 2);
        b.Property(x => x.UpdatedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.ExamId, x.StudentId, x.SubjectId }).IsUnique();
        b.HasOne<PreSchoolExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PreSchoolStudent>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PreSchoolSubject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolStoreItemConfiguration : IEntityTypeConfiguration<PreSchoolStoreItem>
{
    public void Configure(EntityTypeBuilder<PreSchoolStoreItem> b)
    {
        b.ToTable("PreSchoolStoreItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolStoreMovementConfiguration : IEntityTypeConfiguration<PreSchoolStoreMovement>
{
    public void Configure(EntityTypeBuilder<PreSchoolStoreMovement> b)
    {
        b.ToTable("PreSchoolStoreMovement");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(10).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.HasOne(x => x.Item).WithMany(i => i.Movements).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolAttendanceConfiguration : IEntityTypeConfiguration<PreSchoolAttendance>
{
    public void Configure(EntityTypeBuilder<PreSchoolAttendance> b)
    {
        b.ToTable("PreSchoolAttendance");
        b.HasKey(x => x.Id);
        b.Property(x => x.StaffName).HasMaxLength(100);
        b.Property(x => x.StaffRole).HasMaxLength(20);
        b.Property(x => x.LoginId).HasMaxLength(250);
        b.Property(x => x.Direction).HasMaxLength(3).IsRequired();
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.AccuracyMetres).HasPrecision(9, 2);
        b.Property(x => x.DistanceMetres).HasPrecision(9, 2);
        b.HasIndex(x => new { x.StaffId, x.OnDate, x.ScannedOn });
        b.HasOne<PreSchoolStaffMember>().WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolAttendanceChallengeConfiguration : IEntityTypeConfiguration<PreSchoolAttendanceChallenge>
{
    public void Configure(EntityTypeBuilder<PreSchoolAttendanceChallenge> b)
    {
        b.ToTable("PreSchoolAttendanceChallenge");
        b.HasKey(x => x.Id);
        b.Property(x => x.StaffName).HasMaxLength(100);
        b.Property(x => x.StaffRole).HasMaxLength(20);
        b.Property(x => x.LoginId).HasMaxLength(250);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.AccuracyMetres).HasPrecision(9, 2);
        b.Property(x => x.DistanceMetres).HasPrecision(9, 2);
        b.Property(x => x.CodeHash).HasMaxLength(100).IsRequired();
        b.Property(x => x.SentToEmail).HasMaxLength(150);
        b.HasOne<PreSchoolStaffMember>().WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolPhotoConfiguration : IEntityTypeConfiguration<PreSchoolPhoto>
{
    public void Configure(EntityTypeBuilder<PreSchoolPhoto> b)
    {
        b.ToTable("PreSchoolPhoto");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slot).HasMaxLength(20).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Data).IsRequired();
        b.Property(x => x.UpdatedBy).HasMaxLength(250);
        b.HasOne<PreSchoolStudent>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PreSchoolStudentConfiguration : IEntityTypeConfiguration<PreSchoolStudent>
{
    public void Configure(EntityTypeBuilder<PreSchoolStudent> b)
    {
        b.ToTable("PreSchoolStudent");
        b.HasKey(x => x.Id);
        b.Property(x => x.ChildName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Gender).HasMaxLength(10);
        b.Property(x => x.BloodGroup).HasMaxLength(5);
        b.Property(x => x.FatherName).HasMaxLength(100);
        b.Property(x => x.MotherName).HasMaxLength(100);
        b.Property(x => x.ParentName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.EmergencyPhone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.MonthlyDiscount).HasPrecision(10, 2);
        b.Property(x => x.GuardianRelation).HasMaxLength(30);
        b.Property(x => x.Guardian2Name).HasMaxLength(100);
        b.Property(x => x.Guardian2Relation).HasMaxLength(30);
        b.Property(x => x.Guardian2Phone).HasMaxLength(20);
        b.Property(x => x.HeightCm).HasPrecision(5, 1);
        b.Property(x => x.WeightKg).HasPrecision(5, 1);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.AdmissionNo).IsUnique();
        b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolReceiptConfiguration : IEntityTypeConfiguration<PreSchoolReceipt>
{
    public void Configure(EntityTypeBuilder<PreSchoolReceipt> b)
    {
        b.ToTable("PreSchoolReceipt");
        b.HasKey(x => x.Id);
        b.Property(x => x.Mode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Total).HasPrecision(10, 2);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Property(x => x.CancelledBy).HasMaxLength(250);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.HasIndex(x => x.ReceiptNo).IsUnique();
        b.HasIndex(x => x.ReceivedOn);
        b.HasOne(x => x.Student).WithMany(s => s.Receipts).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolReceiptLineConfiguration : IEntityTypeConfiguration<PreSchoolReceiptLine>
{
    public void Configure(EntityTypeBuilder<PreSchoolReceiptLine> b)
    {
        b.ToTable("PreSchoolReceiptLine");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(10).IsRequired();
        b.Property(x => x.Description).HasMaxLength(120).IsRequired();
        b.Property(x => x.Amount).HasPrecision(10, 2);
        b.HasOne(x => x.Receipt).WithMany(r => r.Lines).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PreSchoolStaffConfiguration : IEntityTypeConfiguration<PreSchoolStaffMember>
{
    public void Configure(EntityTypeBuilder<PreSchoolStaffMember> b)
    {
        b.ToTable("PreSchoolStaff");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.MonthlySalary).HasPrecision(10, 2);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}

public class PreSchoolSalaryPaymentConfiguration : IEntityTypeConfiguration<PreSchoolSalaryPayment>
{
    public void Configure(EntityTypeBuilder<PreSchoolSalaryPayment> b)
    {
        b.ToTable("PreSchoolSalaryPayment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Salary).HasPrecision(10, 2);
        b.Property(x => x.DaysAbsent).HasPrecision(5, 1);
        b.Property(x => x.Deduction).HasPrecision(10, 2);
        b.Property(x => x.NetPaid).HasPrecision(10, 2);
        b.Property(x => x.Mode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.StaffId, x.ForMonth }).IsUnique();
        b.HasIndex(x => x.PaidOn);
        b.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreSchoolExpenseConfiguration : IEntityTypeConfiguration<PreSchoolExpense>
{
    public void Configure(EntityTypeBuilder<PreSchoolExpense> b)
    {
        b.ToTable("PreSchoolExpense");
        b.HasKey(x => x.Id);
        b.Property(x => x.Head).HasMaxLength(60).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Amount).HasPrecision(10, 2);
        b.Property(x => x.Mode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => x.SpentOn);
    }
}
