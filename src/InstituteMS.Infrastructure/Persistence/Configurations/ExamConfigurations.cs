using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class ExamPaperConfiguration : IEntityTypeConfiguration<ExamPaper>
{
    public void Configure(EntityTypeBuilder<ExamPaper> b)
    {
        b.ToTable("ExamPaper");
        b.HasKey(x => x.ExamId);

        b.Property(x => x.ExamId).HasColumnName("ExamId");
        b.Property(x => x.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Subject).HasColumnName("Subject").HasMaxLength(100);
        b.Property(x => x.CourseId).HasColumnName("Cid");
        b.Property(x => x.TotalMarks).HasColumnName("TotalMarks");
        b.Property(x => x.PassMarks).HasColumnName("PassMarks");
        b.Property(x => x.DurationMinutes).HasColumnName("DurationMins");
        b.Property(x => x.OpensOn).HasColumnName("OpensOn");
        b.Property(x => x.ClosesOn).HasColumnName("ClosesOn");
        b.Property(x => x.IsPublished).HasColumnName("IsPublished");
        b.Property(x => x.PaperType).HasColumnName("PaperType").HasMaxLength(10);
        b.Property(x => x.TermNumber).HasColumnName("TermNumber");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasOne(x => x.Course)
            .WithMany()
            .HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Questions)
            .WithOne(q => q.Paper!)
            .HasForeignKey(q => q.ExamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExamQuestionConfiguration : IEntityTypeConfiguration<ExamQuestion>
{
    public void Configure(EntityTypeBuilder<ExamQuestion> b)
    {
        b.ToTable("ExamQuestion");
        b.HasKey(x => x.QuestionId);

        b.Property(x => x.QuestionId).HasColumnName("QuestionId");
        b.Property(x => x.ExamId).HasColumnName("ExamId");
        b.Property(x => x.QuestionNo).HasColumnName("QuestionNo");
        b.Property(x => x.QuestionText).HasColumnName("QuestionText").IsRequired();
        b.Property(x => x.OptionA).HasColumnName("OptionA").HasMaxLength(500).IsRequired();
        b.Property(x => x.OptionB).HasColumnName("OptionB").HasMaxLength(500).IsRequired();
        b.Property(x => x.OptionC).HasColumnName("OptionC").HasMaxLength(500);
        b.Property(x => x.OptionD).HasColumnName("OptionD").HasMaxLength(500);
        b.Property(x => x.CorrectOption).HasColumnName("CorrectOption");
        b.Property(x => x.Marks).HasColumnName("Marks");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
    }
}

public class ExamAttemptConfiguration : IEntityTypeConfiguration<ExamAttempt>
{
    public void Configure(EntityTypeBuilder<ExamAttempt> b)
    {
        b.ToTable("ExamAttempt");
        b.HasKey(x => x.AttemptId);

        b.Property(x => x.AttemptId).HasColumnName("AttemptId");
        b.Property(x => x.ExamId).HasColumnName("ExamId");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(15).IsRequired();
        b.Property(x => x.StartedOn).HasColumnName("StartedOn");
        b.Property(x => x.DueOn).HasColumnName("DueOn");
        b.Property(x => x.SubmittedOn).HasColumnName("SubmittedOn");
        b.Property(x => x.Answers).HasColumnName("Answers");
        b.Property(x => x.Score).HasColumnName("Score");
        b.Property(x => x.TotalMarks).HasColumnName("TotalMarks");
        b.Property(x => x.CorrectCount).HasColumnName("CorrectCount");
        b.Property(x => x.QuestionCount).HasColumnName("QuestionCount");
        b.Property(x => x.AutoSubmitted).HasColumnName("AutoSubmitted");
        b.Property(x => x.Source).HasColumnName("Source").HasMaxLength(10);
        b.Property(x => x.EnteredBy).HasColumnName("EnteredBy").HasMaxLength(50);
        b.Property(x => x.CourseId).HasColumnName("Cid");
        b.Property(x => x.TermNumber).HasColumnName("TermNumber");
        b.Property(x => x.PaperType).HasColumnName("PaperType").HasMaxLength(10);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Paper)
            .WithMany()
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExamScheduleConfiguration : IEntityTypeConfiguration<ExamSchedule>
{
    public void Configure(EntityTypeBuilder<ExamSchedule> b)
    {
        b.ToTable("ExamSchedule");
        b.HasKey(x => x.ScheduleId);

        b.Property(x => x.ScheduleId).HasColumnName("ScheduleId");
        b.Property(x => x.ExamId).HasColumnName("ExamId");
        b.Property(x => x.StudentRegistrationId).HasColumnName("StuRegId").HasMaxLength(50).IsRequired();
        b.Property(x => x.ScheduledOn).HasColumnName("ScheduledOn");

        // char(6): held as text so a leading zero survives.
        b.Property(x => x.AccessCode).HasColumnName("AccessCode").HasMaxLength(6).IsFixedLength();

        b.Property(x => x.CodeIssuedOn).HasColumnName("CodeIssuedOn");
        b.Property(x => x.CodeIssuedBy).HasColumnName("CodeIssuedBy").HasMaxLength(50);
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasIndex(x => new { x.ExamId, x.StudentRegistrationId }).IsUnique();

        b.HasOne(x => x.Paper)
            .WithMany()
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
