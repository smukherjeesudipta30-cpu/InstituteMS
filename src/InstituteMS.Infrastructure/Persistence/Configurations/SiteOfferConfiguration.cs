using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class SiteOfferConfiguration : IEntityTypeConfiguration<SiteOffer>
{
    public void Configure(EntityTypeBuilder<SiteOffer> b)
    {
        b.ToTable("SiteOffer");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.Ignore(x => x.EndsOn);
    }
}
