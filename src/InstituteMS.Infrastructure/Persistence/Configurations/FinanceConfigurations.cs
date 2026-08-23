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
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

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
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

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
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");
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
        b.Property(x => x.SourceIp).HasColumnName("SourceIp").HasMaxLength(64);
        b.Property(x => x.IsHandled).HasColumnName("IsHandled");

        b.HasIndex(x => x.SubmittedOn).IsDescending();
    }
}
