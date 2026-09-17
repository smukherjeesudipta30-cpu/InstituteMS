using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class OrgNodeConfiguration : IEntityTypeConfiguration<OrgNode>
{
    public void Configure(EntityTypeBuilder<OrgNode> b)
    {
        b.ToTable("OrgNode");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.PersonName).HasColumnName("PersonName").HasMaxLength(150).IsRequired();
        b.Property(x => x.Designation).HasColumnName("Designation").HasMaxLength(150);
        b.Property(x => x.Qualification).HasColumnName("Qualification").HasMaxLength(200);
        b.Property(x => x.ComputerTraining).HasColumnName("ComputerTraining").HasMaxLength(200);
        b.Property(x => x.IsTechnical).HasColumnName("IsTechnical");
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(150);
        b.Property(x => x.Branch).HasColumnName("Branch").HasMaxLength(100);
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.SortOrder).HasColumnName("SortOrder");
        b.Property(x => x.StaffType).HasColumnName("StaffType").HasMaxLength(20);
        b.Property(x => x.StaffId).HasColumnName("StaffId");
        b.Property(x => x.IsDefaultStaffParent).HasColumnName("IsDefaultStaffParent");
        b.Property(x => x.PhotoFileName).HasColumnName("PhotoFileName").HasMaxLength(200);
        b.Property(x => x.PhotoContentType).HasColumnName("PhotoContentType").HasMaxLength(100);
        b.Property(x => x.PhotoBase64).HasColumnName("PhotoBase64");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");
    }
}

public class OrgEdgeConfiguration : IEntityTypeConfiguration<OrgEdge>
{
    public void Configure(EntityTypeBuilder<OrgEdge> b)
    {
        b.ToTable("OrgEdge");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.ParentId).HasColumnName("ParentId");
        b.Property(x => x.ChildId).HasColumnName("ChildId");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Child).WithMany().HasForeignKey(x => x.ChildId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ParentId, x.ChildId }).IsUnique();
        b.HasIndex(x => x.ChildId).HasDatabaseName("IX_OrgEdge_Child");
    }
}
