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

        // dbo.ImageUpload never actually got CreatedBy/CreatedOn — checked directly
        // against the live table (8 columns, neither present), which is why every
        // photo upload attempt was failing outright with "Invalid column name
        // 'CreatedBy'": EF was building every SELECT against this entity, upload
        // included, with two columns the real table doesn't have. Same fix already
        // applied elsewhere in this file's siblings (Teacher, TeacherExpense) for
        // the identical reason — the properties stay on the C# entity so nothing
        // that reads them needs to change, they are just never sent to or read
        // from the database.
        b.Ignore(x => x.CreatedBy);
        b.Ignore(x => x.CreatedOn);

        b.HasIndex(x => x.StudentRegistrationId);
    }
}
