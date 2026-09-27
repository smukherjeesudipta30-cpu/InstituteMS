using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("Holiday");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Category).HasMaxLength(20).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
        b.HasIndex(x => new { x.HolidayDate, x.Name }).IsUnique();
    }
}

public class HolidayYearImportConfiguration : IEntityTypeConfiguration<HolidayYearImport>
{
    public void Configure(EntityTypeBuilder<HolidayYearImport> b)
    {
        b.ToTable("HolidayYearImport");
        b.HasKey(x => x.Year);
        b.Property(x => x.Year).ValueGeneratedNever();
        b.Property(x => x.Source).HasMaxLength(100).IsRequired();
        b.Property(x => x.ImportedBy).HasMaxLength(250);
    }
}
