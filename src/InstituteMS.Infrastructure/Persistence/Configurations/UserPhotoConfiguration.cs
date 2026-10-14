using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class UserPhotoConfiguration : IEntityTypeConfiguration<UserPhoto>
{
    public void Configure(EntityTypeBuilder<UserPhoto> b)
    {
        b.ToTable("UserPhoto");
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.ContentType).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(x => x.PhotoBase64).IsRequired();
        b.Property(x => x.ModifiedBy).HasMaxLength(250).IsUnicode(false);
    }
}
