using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class StudentPhotoConfiguration : IEntityTypeConfiguration<StudentPhoto>
{
    public void Configure(EntityTypeBuilder<StudentPhoto> b)
    {
        b.ToTable("ImageUpload");
        b.HasKey(x => x.FileId);

        b.Property(x => x.FileId).HasColumnName("FileID");
        b.Property(x => x.FileName).HasColumnName("FileName").HasMaxLength(100);
        b.Property(x => x.Description).HasColumnName("Description");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(15);
        b.Property(x => x.FilePath).HasColumnName("FilePath");
        b.Property(x => x.IsInline).HasColumnName("IsInline");
        b.Property(x => x.FileSize).HasColumnName("FileSize");
        b.Property(x => x.ContentType).HasColumnName("ContentType").HasMaxLength(100);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasIndex(x => x.StudentRegistrationId);
    }
}
