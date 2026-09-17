using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class AdminNotificationConfiguration : IEntityTypeConfiguration<AdminNotification>
{
    public void Configure(EntityTypeBuilder<AdminNotification> b)
    {
        b.ToTable("AdminNotification");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.Type).HasColumnName("Type").HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasColumnName("Title").HasMaxLength(300).IsRequired();
        b.Property(x => x.StudentName).HasColumnName("StudentName").HasMaxLength(250);
        b.Property(x => x.CourseName).HasColumnName("CourseName").HasMaxLength(250);
        b.Property(x => x.RegistrationId).HasColumnName("RegistrationId").HasMaxLength(50);
        b.Property(x => x.LoginId).HasColumnName("LoginId").HasMaxLength(250);
        b.Property(x => x.DetailValue).HasColumnName("DetailValue").HasMaxLength(300);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ReadOn).HasColumnName("ReadOn");
        b.Property(x => x.ReadBy).HasColumnName("ReadBy").HasMaxLength(250);

        // Computed from ReadOn; there is no such column.
        b.Ignore(x => x.IsRead);

        b.HasIndex(x => new { x.ReadOn, x.CreatedOn });
    }
}
