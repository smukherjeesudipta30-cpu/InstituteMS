using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class SiteVisitConfiguration : IEntityTypeConfiguration<SiteVisit>
{
    public void Configure(EntityTypeBuilder<SiteVisit> b)
    {
        b.ToTable("SiteVisit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Path).HasMaxLength(100).IsRequired();
        b.Property(x => x.Source).HasMaxLength(30).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(10).IsRequired();
        b.Property(x => x.VisitorKey).HasMaxLength(32).IsRequired();
    }
}
