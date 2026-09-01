using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class CourseEnquiryConfiguration : IEntityTypeConfiguration<CourseEnquiry>
{
    public void Configure(EntityTypeBuilder<CourseEnquiry> b)
    {
        b.ToTable("CourseEnquiry");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.StudentName).HasColumnName("StudentName").HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20).IsRequired();
        b.Property(x => x.Address).HasColumnName("Address").HasMaxLength(500);
        b.Property(x => x.CourseId).HasColumnName("CourseId");
        b.Property(x => x.Remarks).HasColumnName("Remarks").HasMaxLength(1000);
        b.Property(x => x.EnquiryDate).HasColumnName("EnquiryDate");
        b.Property(x => x.Status).HasColumnName("Status").HasMaxLength(20).IsRequired();
        b.Property(x => x.NextFollowUpOn).HasColumnName("NextFollowUpOn");
        b.Property(x => x.LastFollowUpOn).HasColumnName("LastFollowUpOn");
        b.Property(x => x.FollowUpCount).HasColumnName("FollowUpCount");
        b.Property(x => x.BranchId).HasColumnName("BranchId");
        b.Property(x => x.ConvertedStudentRegistrationId)
            .HasColumnName("ConvertedStudentRegistrationId").HasMaxLength(50);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(250);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(250);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        // Computed from Status; there is no such column.
        b.Ignore(x => x.IsOpen);

        /* No foreign key, matching the table. A course removed from the catalogue
           must not take the enquiries about it with it, and people ask about
           things that are not in the catalogue at all. */
        b.HasOne(x => x.Course)
            .WithMany()
            .HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);

        b.HasIndex(x => new { x.Status, x.NextFollowUpOn });
        b.HasIndex(x => x.EnquiryDate);
        b.HasIndex(x => x.Phone);
    }
}
