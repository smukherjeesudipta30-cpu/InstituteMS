using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> b)
    {
        b.ToTable("Competition");
        b.HasKey(x => x.Id);

        b.Property(x => x.Year).HasColumnName("Year");
        b.Property(x => x.EventDate).HasColumnName("EventDate");
        b.Property(x => x.Venue).HasColumnName("Venue").HasMaxLength(200);
        b.Property(x => x.VenueBengali).HasColumnName("VenueBengali").HasMaxLength(200);

        b.Property(x => x.DrawingFee).HasColumnName("DrawingFee").HasPrecision(10, 2);
        b.Property(x => x.DanceFee).HasColumnName("DanceFee").HasPrecision(10, 2);

        b.Property(x => x.DrawingStartTime).HasColumnName("DrawingStartTime");
        b.Property(x => x.DanceStartTime).HasColumnName("DanceStartTime");
        b.Property(x => x.PrizeGivingTime).HasColumnName("PrizeGivingTime");

        b.Property(x => x.DrawingRulesEnglish).HasColumnName("DrawingRulesEnglish");
        b.Property(x => x.DrawingRulesBengali).HasColumnName("DrawingRulesBengali");
        b.Property(x => x.DanceRulesEnglish).HasColumnName("DanceRulesEnglish");
        b.Property(x => x.DanceRulesBengali).HasColumnName("DanceRulesBengali");
        b.Property(x => x.NoticeEnglish).HasColumnName("NoticeEnglish");
        b.Property(x => x.NoticeBengali).HasColumnName("NoticeBengali");

        b.Property(x => x.RegistrationOpensOn).HasColumnName("RegistrationOpensOn");
        b.Property(x => x.RegistrationClosesOn).HasColumnName("RegistrationClosesOn");

        b.Property(x => x.ContactPhones).HasColumnName("ContactPhones").HasMaxLength(200);
        b.Property(x => x.IsActive).HasColumnName("IsActive");

        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => x.Year).IsUnique();

        b.HasMany(x => x.Groups)
            .WithOne(x => x.Competition)
            .HasForeignKey(x => x.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Organisers)
            .WithOne(x => x.Competition)
            .HasForeignKey(x => x.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompetitionGroupConfiguration : IEntityTypeConfiguration<CompetitionGroup>
{
    public void Configure(EntityTypeBuilder<CompetitionGroup> b)
    {
        b.ToTable("CompetitionGroup");
        b.HasKey(x => x.Id);

        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.Kind).HasColumnName("Kind").HasMaxLength(10).IsRequired();
        b.Property(x => x.Code).HasColumnName("Code").HasMaxLength(20).IsRequired();
        b.Property(x => x.LabelEnglish).HasColumnName("LabelEnglish").HasMaxLength(120).IsRequired();
        b.Property(x => x.LabelBengali).HasColumnName("LabelBengali").HasMaxLength(120);
        b.Property(x => x.MinAge).HasColumnName("MinAge");
        b.Property(x => x.MaxAge).HasColumnName("MaxAge");
        b.Property(x => x.MinClass).HasColumnName("MinClass");
        b.Property(x => x.MaxClass).HasColumnName("MaxClass");
        b.Property(x => x.Capacity).HasColumnName("Capacity");
        b.Property(x => x.SortOrder).HasColumnName("SortOrder");
    }
}

public class CompetitionOrganiserConfiguration : IEntityTypeConfiguration<CompetitionOrganiser>
{
    public void Configure(EntityTypeBuilder<CompetitionOrganiser> b)
    {
        b.ToTable("CompetitionOrganiser");
        b.HasKey(x => x.Id);

        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.Name).HasColumnName("Name").HasMaxLength(150).IsRequired();
        b.Property(x => x.Designation).HasColumnName("Designation").HasMaxLength(150);
        b.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
        b.Property(x => x.SortOrder).HasColumnName("SortOrder");
        b.Property(x => x.IsActive).HasColumnName("IsActive");
    }
}

public class CompetitionApplicantConfiguration : IEntityTypeConfiguration<CompetitionApplicant>
{
    public void Configure(EntityTypeBuilder<CompetitionApplicant> b)
    {
        b.ToTable("CompetitionApplicant");
        b.HasKey(x => x.Id);

        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.ParticipantName).HasColumnName("ParticipantName").HasMaxLength(150).IsRequired();
        b.Property(x => x.GuardianName).HasColumnName("GuardianName").HasMaxLength(150);
        b.Property(x => x.DateOfBirth).HasColumnName("DateOfBirth");
        b.Property(x => x.SchoolName).HasColumnName("SchoolName").HasMaxLength(200);
        b.Property(x => x.ClassName).HasColumnName("ClassName").HasMaxLength(50);
        b.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(150);
        b.Property(x => x.Address).HasColumnName("Address").HasMaxLength(400);

        b.Property(x => x.PaymentMode).HasColumnName("PaymentMode").HasMaxLength(10).IsRequired();
        b.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(10, 2);
        b.Property(x => x.TransactionRef).HasColumnName("TransactionRef").HasMaxLength(64);
        b.Property(x => x.PaymentStatus).HasColumnName("PaymentStatus").HasMaxLength(12).IsRequired();
        b.Property(x => x.CancelReason).HasColumnName("CancelReason").HasMaxLength(300);
        b.Property(x => x.ConfirmedBy).HasColumnName("ConfirmedBy").HasMaxLength(50);
        b.Property(x => x.ConfirmedOn).HasColumnName("ConfirmedOn");

        b.Property(x => x.SubmittedOn).HasColumnName("SubmittedOn");
        b.Property(x => x.SourceIp).HasColumnName("SourceIp").HasMaxLength(45);

        b.HasMany(x => x.Entries)
            .WithOne(x => x.Applicant)
            .HasForeignKey(x => x.ApplicantId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Documents)
            .WithOne(x => x.Applicant)
            .HasForeignKey(x => x.ApplicantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CompetitionEntryConfiguration : IEntityTypeConfiguration<CompetitionEntry>
{
    public void Configure(EntityTypeBuilder<CompetitionEntry> b)
    {
        b.ToTable("CompetitionEntry");
        b.HasKey(x => x.Id);

        b.Property(x => x.ApplicantId).HasColumnName("ApplicantId");
        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.Kind).HasColumnName("Kind").HasMaxLength(10).IsRequired();
        b.Property(x => x.GroupId).HasColumnName("GroupId");
        b.Property(x => x.Serial).HasColumnName("Serial");
        b.Property(x => x.RegistrationNo).HasColumnName("RegistrationNo").HasMaxLength(40).IsRequired();
        b.Property(x => x.BatchNo).HasColumnName("BatchNo");
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.RegistrationNo).IsUnique();
        b.HasIndex(x => new { x.CompetitionId, x.Kind, x.Serial }).IsUnique();
    }
}

public class CompetitionDocumentConfiguration : IEntityTypeConfiguration<CompetitionDocument>
{
    public void Configure(EntityTypeBuilder<CompetitionDocument> b)
    {
        b.ToTable("CompetitionDocument");
        b.HasKey(x => x.Id);

        b.Property(x => x.ApplicantId).HasColumnName("ApplicantId");
        b.Property(x => x.Kind).HasColumnName("Kind").HasMaxLength(20).IsRequired();
        b.Property(x => x.FileName).HasColumnName("FileName").HasMaxLength(200);
        b.Property(x => x.ContentType).HasColumnName("ContentType").HasMaxLength(100);
        b.Property(x => x.Content).HasColumnName("Content").IsRequired();
        b.Property(x => x.UploadedOn).HasColumnName("UploadedOn");
    }
}

public class CompetitionAdvertConfiguration : IEntityTypeConfiguration<CompetitionAdvert>
{
    public void Configure(EntityTypeBuilder<CompetitionAdvert> b)
    {
        b.ToTable("CompetitionAdvert");
        b.HasKey(x => x.Id);

        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.Slot).HasColumnName("Slot").HasMaxLength(20).IsRequired();
        b.Property(x => x.FileName).HasColumnName("FileName").HasMaxLength(200);
        b.Property(x => x.ContentType).HasColumnName("ContentType").HasMaxLength(100);
        b.Property(x => x.Content).HasColumnName("Content").IsRequired();
        b.Property(x => x.Width).HasColumnName("Width");
        b.Property(x => x.Height).HasColumnName("Height");
        b.Property(x => x.UploadedBy).HasColumnName("UploadedBy").HasMaxLength(50);
        b.Property(x => x.UploadedOn).HasColumnName("UploadedOn");

        b.HasIndex(x => new { x.CompetitionId, x.Slot }).IsUnique();
    }
}

public class CompetitionDraftConfiguration : IEntityTypeConfiguration<CompetitionDraft>
{
    public void Configure(EntityTypeBuilder<CompetitionDraft> b)
    {
        b.ToTable("CompetitionDraft");
        b.HasKey(x => x.Id);

        b.Property(x => x.CompetitionId).HasColumnName("CompetitionId");
        b.Property(x => x.RegistrationCode).HasColumnName("RegistrationCode").HasMaxLength(30).IsRequired();
        b.Property(x => x.ParticipantName).HasColumnName("ParticipantName").HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(15).IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("PasswordHash").HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(50);
        b.Property(x => x.Status).HasColumnName("Status").HasMaxLength(20).IsRequired();
        b.Property(x => x.LinkedApplicantId).HasColumnName("LinkedApplicantId");
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.LastAccessedOn).HasColumnName("LastAccessedOn");
        b.Property(x => x.PasswordResetCodeHash).HasColumnName("PasswordResetCodeHash").HasMaxLength(255);
        b.Property(x => x.PasswordResetCodeExpiry).HasColumnName("PasswordResetCodeExpiry");
        b.Property(x => x.PasswordResetAttempts).HasColumnName("PasswordResetAttempts");

        b.HasIndex(x => x.RegistrationCode).IsUnique();
    }
}
