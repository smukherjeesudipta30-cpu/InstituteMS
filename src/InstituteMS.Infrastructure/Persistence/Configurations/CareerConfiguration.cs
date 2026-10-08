using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class JobVacancyConfiguration : IEntityTypeConfiguration<JobVacancy>
{
    public void Configure(EntityTypeBuilder<JobVacancy> b)
    {
        b.ToTable("JobVacancy");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(100).IsRequired();
        b.Property(x => x.ShiftName).HasMaxLength(50);
        b.Property(x => x.WorkingDays).HasMaxLength(100);
        b.Property(x => x.Qualification).HasMaxLength(300).IsRequired();
        b.Property(x => x.Experience).HasMaxLength(200);
        b.Property(x => x.Salary).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.CreatedBy).HasMaxLength(250);
        b.Property(x => x.ModifiedBy).HasMaxLength(250);
    }
}

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("JobApplication");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(15).IsRequired();
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.Qualification).HasMaxLength(300).IsRequired();
        b.Property(x => x.Experience).HasMaxLength(300);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.CvFileName).HasMaxLength(200);
        b.Property(x => x.CvContentType).HasMaxLength(100);
        b.Property(x => x.CvContent).IsRequired();
        b.Property(x => x.PhotoContentType).HasMaxLength(50);
        b.Property(x => x.PhotoContent).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.AdminNote).HasMaxLength(500);
        b.Property(x => x.ReviewedBy).HasMaxLength(250);
        b.HasOne(x => x.Vacancy).WithMany().HasForeignKey(x => x.VacancyId);
    }
}
