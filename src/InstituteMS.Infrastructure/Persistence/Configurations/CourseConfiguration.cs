using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> b)
    {
        b.ToTable("Course");
        b.HasKey(x => x.Cid);

        b.Property(x => x.Cid).HasColumnName("Cid");
        b.Property(x => x.Name).HasColumnName("CName").HasMaxLength(300);
        b.Property(x => x.Duration).HasColumnName("Duration");
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
