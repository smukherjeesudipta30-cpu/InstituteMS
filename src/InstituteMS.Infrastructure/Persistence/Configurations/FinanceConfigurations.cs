using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expense");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Details).HasColumnName("ExDetails");
        b.Property(x => x.ExpenseDate).HasColumnName("ExDate");
        b.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2);
        // Branch and audit live on dbo.ExpenseExt - see ExpenseExtConfiguration.
        b.HasOne(x => x.Ext)
            .WithOne()
            .HasForeignKey<ExpenseExt>(x => x.ExpenseId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.ExpenseDate);
    }
}

public class OtherIncomeConfiguration : IEntityTypeConfiguration<OtherIncome>
{
    public void Configure(EntityTypeBuilder<OtherIncome> b)
    {
        b.ToTable("OIncome");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("ID");
        b.Property(x => x.Purpose).HasColumnName("IPurpose");
        b.Property(x => x.Amount).HasColumnName("IAmount").HasPrecision(18, 2);
        b.Property(x => x.IncomeDate).HasColumnName("ODate");
        // Branch and audit live on dbo.OIncomeExt - see OtherIncomeExtConfiguration.
        b.HasOne(x => x.Ext)
            .WithOne()
            .HasForeignKey<OtherIncomeExt>(x => x.IncomeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.IncomeDate);
    }
}

public class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    public void Configure(EntityTypeBuilder<Notice> b)
    {
        b.ToTable("Notice");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("NID");
        b.Property(x => x.Text).HasColumnName("NewNotice");
        b.Property(x => x.NoticeDate).HasColumnName("NoticeDate");
        // Notice is one of the seven tables the legacy application inserts into
        // without a column list, so it cannot gain audit columns.
        b.Ignore(x => x.CreatedBy);
        b.Ignore(x => x.CreatedOn);
        b.Ignore(x => x.ModifiedBy);
        b.Ignore(x => x.ModifiedOn);
    }
}

public class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> b)
    {
        b.ToTable("CusMessage");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("CusName").HasMaxLength(200);
        b.Property(x => x.Email).HasColumnName("CusMailid").HasMaxLength(200);
        b.Property(x => x.Phone).HasColumnName("CusPhone").HasMaxLength(20);
        b.Property(x => x.Body).HasColumnName("cusbody");
        b.Property(x => x.SubmittedOn).HasColumnName("SubDate");
        // CusMessage is bare-inserted by the legacy application too, so neither
        // column can be added to it. IsHandled lives on dbo.CusMessageExt, reached
        // through Ext; SourceIp is simply not recorded.
        b.Ignore(x => x.SourceIp);
        b.Ignore(x => x.IsHandled);

        b.HasOne(x => x.Ext)
            .WithOne()
            .HasForeignKey<ContactMessageExt>(x => x.MessageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.SubmittedOn).IsDescending();
    }
}

/// <summary>
/// The weekly class routine behind the front page's "Show Timing" panel.
/// Legacy column names are single words per weekday, some abbreviated.
/// </summary>
public class DailyRoutineConfiguration : IEntityTypeConfiguration<DailyRoutine>
{
    public void Configure(EntityTypeBuilder<DailyRoutine> b)
    {
        b.ToTable("dailyRoutine");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(250);
        b.Property(x => x.Sunday).HasColumnName("sun").HasMaxLength(250);
        b.Property(x => x.Monday).HasColumnName("mon").HasMaxLength(250);
        b.Property(x => x.Tuesday).HasColumnName("tues").HasMaxLength(250);
        b.Property(x => x.Wednesday).HasColumnName("wed").HasMaxLength(250);
        b.Property(x => x.Thursday).HasColumnName("thurs").HasMaxLength(250);
        b.Property(x => x.Friday).HasColumnName("fri").HasMaxLength(250);
        b.Property(x => x.Saturday).HasColumnName("sat").HasMaxLength(250);
    }
}
