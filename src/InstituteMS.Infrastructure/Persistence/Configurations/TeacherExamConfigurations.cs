using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteMS.Infrastructure.Persistence.Configurations;

public class TeacherExamAssignmentConfiguration : IEntityTypeConfiguration<TeacherExamAssignment>
{
    public void Configure(EntityTypeBuilder<TeacherExamAssignment> b)
    {
        b.ToTable("TeacherExamAssignment");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.TeacherId).HasColumnName("TeacherId");
        b.Property(x => x.CourseId).HasColumnName("CourseId");
        b.Property(x => x.IsActive).HasColumnName("IsActive");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");
        b.Property(x => x.ModifiedBy).HasColumnName("ModifiedBy").HasMaxLength(50);
        b.Property(x => x.ModifiedOn).HasColumnName("ModifiedOn");

        b.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId);
        b.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId);

        // One standing assignment per teacher per course — a second attempt to
        // assign the same pair corrects the existing row instead of duplicating it.
        b.HasIndex(x => new { x.TeacherId, x.CourseId }).IsUnique();
    }
}

public class TeacherExamScheduleConfiguration : IEntityTypeConfiguration<TeacherExamSchedule>
{
    public void Configure(EntityTypeBuilder<TeacherExamSchedule> b)
    {
        b.ToTable("TeacherExamSchedule");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.TeacherExamAssignmentId).HasColumnName("TeacherExamAssignmentId");
        b.Property(x => x.ScheduledOn).HasColumnName("ScheduledOn");
        b.Property(x => x.StudyGuide).HasColumnName("StudyGuide");
        b.Property(x => x.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Assignment)
            .WithMany(a => a.Schedules)
            .HasForeignKey(x => x.TeacherExamAssignmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherExamQuestionConfiguration : IEntityTypeConfiguration<TeacherExamQuestion>
{
    public void Configure(EntityTypeBuilder<TeacherExamQuestion> b)
    {
        b.ToTable("TeacherExamQuestion");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.TeacherExamScheduleId).HasColumnName("TeacherExamScheduleId");
        b.Property(x => x.OrderNo).HasColumnName("OrderNo");
        b.Property(x => x.QuestionText).HasColumnName("QuestionText").HasMaxLength(1000).IsRequired();
        b.Property(x => x.OptionA).HasColumnName("OptionA").HasMaxLength(500).IsRequired();
        b.Property(x => x.OptionB).HasColumnName("OptionB").HasMaxLength(500).IsRequired();
        b.Property(x => x.OptionC).HasColumnName("OptionC").HasMaxLength(500).IsRequired();
        b.Property(x => x.OptionD).HasColumnName("OptionD").HasMaxLength(500).IsRequired();
        b.Property(x => x.CorrectOption).HasColumnName("CorrectOption").HasMaxLength(1).IsRequired();
        b.Property(x => x.CreatedOn).HasColumnName("CreatedOn");

        b.HasOne(x => x.Schedule)
            .WithMany(s => s.Questions)
            .HasForeignKey(x => x.TeacherExamScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherExamAttemptConfiguration : IEntityTypeConfiguration<TeacherExamAttempt>
{
    public void Configure(EntityTypeBuilder<TeacherExamAttempt> b)
    {
        b.ToTable("TeacherExamAttempt");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.TeacherExamScheduleId).HasColumnName("TeacherExamScheduleId");
        b.Property(x => x.StartedOn).HasColumnName("StartedOn");
        b.Property(x => x.SubmittedOn).HasColumnName("SubmittedOn");
        b.Property(x => x.ScorePercent).HasColumnName("ScorePercent").HasPrecision(5, 2);
        b.Property(x => x.Passed).HasColumnName("Passed");
        b.Property(x => x.ViolationCount).HasColumnName("ViolationCount");

        // One attempt per sitting.
        b.HasIndex(x => x.TeacherExamScheduleId).IsUnique();

        b.HasOne(x => x.Schedule)
            .WithOne(s => s.Attempt)
            .HasForeignKey<TeacherExamAttempt>(x => x.TeacherExamScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherExamAnswerConfiguration : IEntityTypeConfiguration<TeacherExamAnswer>
{
    public void Configure(EntityTypeBuilder<TeacherExamAnswer> b)
    {
        b.ToTable("TeacherExamAnswer");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("Id");
        b.Property(x => x.TeacherExamAttemptId).HasColumnName("TeacherExamAttemptId");
        b.Property(x => x.TeacherExamQuestionId).HasColumnName("TeacherExamQuestionId");
        b.Property(x => x.SelectedOption).HasColumnName("SelectedOption").HasMaxLength(1);
        b.Property(x => x.IsCorrect).HasColumnName("IsCorrect");

        b.HasOne(x => x.Attempt)
            .WithMany(a => a.Answers)
            .HasForeignKey(x => x.TeacherExamAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Question)
            .WithMany()
            .HasForeignKey(x => x.TeacherExamQuestionId);

        b.HasIndex(x => new { x.TeacherExamAttemptId, x.TeacherExamQuestionId }).IsUnique();
    }
}
