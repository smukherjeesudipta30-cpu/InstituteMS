using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class CertificateIssueConfiguration : IEntityTypeConfiguration<CertificateIssue>
{
    public void Configure(EntityTypeBuilder<CertificateIssue> b)
    {
        b.ToTable("CertificateIssue");
        b.HasKey(x => x.IssueId);

        b.Property(x => x.IssueId).HasColumnName("IssueId");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(50).IsRequired();
        b.Property(x => x.IssueNo).HasColumnName("IssueNo");
        b.Property(x => x.IssuedOn).HasColumnName("IssuedOn");
        b.Property(x => x.IssuedTo).HasColumnName("IssuedTo").HasMaxLength(200);

        b.HasIndex(x => new { x.StudentRegistrationId, x.IssueNo }).IsUnique();
    }
}
