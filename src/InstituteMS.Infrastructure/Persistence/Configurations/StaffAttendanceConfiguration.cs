using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class StaffAttendanceConfiguration : IEntityTypeConfiguration<StaffAttendance>
{
    public void Configure(EntityTypeBuilder<StaffAttendance> b)
    {
        b.ToTable("StaffAttendance");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.StaffType).HasColumnName("StaffType").HasMaxLength(20).IsRequired();
        b.Property(x => x.StaffId).HasColumnName("StaffId");
        b.Property(x => x.LoginId).HasColumnName("LoginId").HasMaxLength(100);
        b.Property(x => x.StaffName).HasColumnName("StaffName").HasMaxLength(150);
        b.Property(x => x.BranchId).HasColumnName("BranchId");
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.ScannedOn).HasColumnName("ScannedOn");
        b.Property(x => x.OnDate).HasColumnName("OnDate");
        b.Property(x => x.Direction).HasColumnName("Direction").HasMaxLength(3).IsRequired();
        b.Property(x => x.Latitude).HasColumnName("Latitude").HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasColumnName("Longitude").HasPrecision(9, 6);
        b.Property(x => x.AccuracyMetres).HasColumnName("AccuracyMetres").HasPrecision(9, 2);
        b.Property(x => x.DistanceMetres).HasColumnName("DistanceMetres").HasPrecision(9, 2);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => new { x.StaffType, x.StaffId, x.OnDate })
            .HasDatabaseName("IX_StaffAttendance_Staff_Date");

        b.HasIndex(x => new { x.OnDate, x.Branch })
            .HasDatabaseName("IX_StaffAttendance_Date_Branch");
    }
}

public class AttendanceShiftConfiguration : IEntityTypeConfiguration<AttendanceShift>
{
    public void Configure(EntityTypeBuilder<AttendanceShift> b)
    {
        b.ToTable("AttendanceShift");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.Name).HasColumnName("Name").HasMaxLength(50).IsRequired();
        b.Property(x => x.StartTime).HasColumnName("StartTime");
        b.Property(x => x.EndTime).HasColumnName("EndTime");
        b.Property(x => x.ScanOutBy).HasColumnName("ScanOutBy");
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class StaffShiftAssignmentConfiguration : IEntityTypeConfiguration<StaffShiftAssignment>
{
    public void Configure(EntityTypeBuilder<StaffShiftAssignment> b)
    {
        b.ToTable("StaffShiftAssignment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.StaffType).HasColumnName("StaffType").HasMaxLength(20).IsRequired();
        b.Property(x => x.StaffId).HasColumnName("StaffId");
        b.Property(x => x.DayOfWeek).HasColumnName("DayOfWeek");
        b.Property(x => x.ShiftId).HasColumnName("ShiftId");
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId);

        b.HasIndex(x => new { x.StaffType, x.StaffId, x.DayOfWeek })
            .HasDatabaseName("IX_StaffShiftAssignment_Staff");
    }
}

public class AttendanceChallengeConfiguration : IEntityTypeConfiguration<AttendanceChallenge>
{
    public void Configure(EntityTypeBuilder<AttendanceChallenge> b)
    {
        b.ToTable("AttendanceChallenge");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.StaffType).HasColumnName("StaffType").HasMaxLength(20).IsRequired();
        b.Property(x => x.StaffId).HasColumnName("StaffId");
        b.Property(x => x.StaffName).HasColumnName("StaffName").HasMaxLength(150);
        b.Property(x => x.LoginId).HasColumnName("LoginId").HasMaxLength(100);
        b.Property(x => x.BranchId).HasColumnName("BranchId");
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.MarkedOn).HasColumnName("MarkedOn");
        b.Property(x => x.OnDate).HasColumnName("OnDate");
        b.Property(x => x.Latitude).HasColumnName("Latitude").HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasColumnName("Longitude").HasPrecision(9, 6);
        b.Property(x => x.AccuracyMetres).HasColumnName("AccuracyMetres").HasPrecision(9, 2);
        b.Property(x => x.DistanceMetres).HasColumnName("DistanceMetres").HasPrecision(9, 2);
        b.Property(x => x.CodeHash).HasColumnName("CodeHash").HasMaxLength(200).IsRequired();
        b.Property(x => x.SentToEmail).HasColumnName("SentToEmail").HasMaxLength(150);
        b.Property(x => x.ExpiresOn).HasColumnName("ExpiresOn");
        b.Property(x => x.Attempts).HasColumnName("Attempts");
        b.Property(x => x.ConsumedOn).HasColumnName("ConsumedOn");
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => new { x.StaffType, x.StaffId, x.ConsumedOn })
            .HasDatabaseName("IX_AttendanceChallenge_Open");
    }
}
