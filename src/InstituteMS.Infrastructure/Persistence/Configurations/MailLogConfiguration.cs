using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>dbo.MailLog_registration_time</c> — the legacy registration mail log.
/// </summary>
/// <remarks>
/// The legacy application never inserts into this table with a column-list-free
/// INSERT, so the coexistence script was able to add audit columns to it and the
/// new application can write to it freely.
/// </remarks>
public class MailLogConfiguration : IEntityTypeConfiguration<MailLogEntry>
{
    public void Configure(EntityTypeBuilder<MailLogEntry> b)
    {
        b.ToTable("MailLog_registration_time");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("MailLogId");
        b.Property(x => x.ForRegistrationId).HasColumnName("For_registraion_id").HasMaxLength(150);
        b.Property(x => x.ToEmail).HasColumnName("ToEmail").HasMaxLength(500);
        b.Property(x => x.Subject).HasColumnName("Subject").HasMaxLength(1000);
        b.Property(x => x.MailBody).HasColumnName("MailBody");
        b.Property(x => x.Status).HasColumnName("Status").HasMaxLength(50);
        b.Property(x => x.ErrorMessage).HasColumnName("ErrorMessage");
        b.Property(x => x.CreatedDate).HasColumnName("CreatedDate");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);

        b.HasIndex(x => x.CreatedDate).IsDescending();
    }
}
