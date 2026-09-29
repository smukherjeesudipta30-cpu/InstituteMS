using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> b)
    {
        b.ToTable("Teacher");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("TecName").HasMaxLength(50);
        b.Property(x => x.LoginId).HasColumnName("LoginId").HasMaxLength(50);
        b.Property(x => x.Phone).HasColumnName("TecPhone").HasMaxLength(10);
        b.Property(x => x.PersonalEmail).HasColumnName("PersonalEmail").HasMaxLength(150);
        b.Property(x => x.Designation).HasColumnName("Designation").HasMaxLength(150);
        b.Property(x => x.Qualification).HasColumnName("Qualification").HasMaxLength(200);
        b.Property(x => x.ComputerTraining).HasColumnName("ComputerTraining").HasMaxLength(200);
        b.Property(x => x.IsTechnical).HasColumnName("IsTechnical");
        b.Property(x => x.NoFeeShare).HasColumnName("NoFeeShare");
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        // The legacy application inserts into this table with "INSERT INTO t
        // VALUES (...)" and no column list, so it cannot gain audit columns
        // without that statement failing. The properties stay on the entity and
        // simply are not persisted.
        b.Ignore(x => x.CreatedBy);
        b.Ignore(x => x.CreatedOn);
        b.Ignore(x => x.ModifiedBy);
        b.Ignore(x => x.ModifiedOn);
    }
}
