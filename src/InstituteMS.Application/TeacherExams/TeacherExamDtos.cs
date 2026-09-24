using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.TeacherExams;

/* ============================================================== assignments */

public record TeacherExamAssignmentSaveRequest
{
    public int TeacherId { get; init; }
    public int CourseId { get; init; }
}

public class TeacherExamAssignmentSaveRequestValidator : AbstractValidator<TeacherExamAssignmentSaveRequest>
{
    public TeacherExamAssignmentSaveRequestValidator()
    {
        RuleFor(x => x.TeacherId).GreaterThan(0).WithMessage("Choose a teacher.");
        RuleFor(x => x.CourseId).GreaterThan(0).WithMessage("Choose a course.");
    }
}

/// <summary>
/// One standing assignment, with when it is next due — worked out live, never
/// stored. See <see cref="ITeacherExamService.GetAssignmentsAsync"/>.
/// </summary>
public record TeacherExamAssignmentDto(
    int Id,
    int TeacherId,
    string TeacherName,
    int CourseId,
    string CourseName,
    bool IsActive,

    /// <summary>Null until a sitting has ever been completed.</summary>
    DateTime? LastCompletedOn,
    decimal? LastScorePercent,
    bool? LastPassed,

    /// <summary>The date/time Admin has already set for the next sitting, if any is pending.</summary>
    DateTime? PendingScheduleOn,

    /// <summary>
    /// True once this assignment needs a new sitting scheduled: no sitting is
    /// currently pending, and either none has ever been taken, the last one was
    /// failed, or three months have passed since the last pass.
    /// </summary>
    bool NeedsScheduling,

    /// <summary>When it became (or will become) due — three months after the last pass, or immediately after a fail/first assignment.</summary>
    DateTime DueOn);

/* ================================================================ schedules */

public record TeacherExamScheduleRequest
{
    public int TeacherExamAssignmentId { get; init; }
    public DateTime ScheduledOn { get; init; }

    /// <summary>How many MCQs to generate for this sitting.</summary>
    public int QuestionCount { get; init; } = 10;
}

public class TeacherExamScheduleRequestValidator : AbstractValidator<TeacherExamScheduleRequest>
{
    public TeacherExamScheduleRequestValidator()
    {
        RuleFor(x => x.TeacherExamAssignmentId).GreaterThan(0).WithMessage("Choose an assignment.");
        RuleFor(x => x.QuestionCount).InclusiveBetween(5, 25).WithMessage("Choose between 5 and 25 questions.");
    }
}

public record TeacherExamScheduleDto(
    int Id,
    int TeacherExamAssignmentId,
    int TeacherId,
    string TeacherName,
    int CourseId,
    string CourseName,
    DateTime ScheduledOn,
    int QuestionCount,

    /// <summary>Upcoming, Ready, Completed — see <see cref="ITeacherExamService.GetSchedulesAsync"/>.</summary>
    string Status,

    decimal? ScorePercent,
    bool? Passed,
    DateTime? SubmittedOn,

    /// <summary>Null if generation failed or has not run yet — never blocks the sitting.</summary>
    string? StudyGuide);

/* ================================================================ taking it */

/// <summary>What the teacher sees while sitting the exam — never the correct option.</summary>
public record TeacherExamQuestionDto(
    int Id,
    int OrderNo,
    string QuestionText,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD);

public record TeacherExamPaperDto(
    int ScheduleId,
    string CourseName,
    DateTime ScheduledOn,
    int AttemptId,
    DateTime StartedOn,
    IReadOnlyList<TeacherExamQuestionDto> Questions);

public record TeacherExamAnswerRequest
{
    public int QuestionId { get; init; }

    /// <summary><c>A</c>, <c>B</c>, <c>C</c> or <c>D</c>.</summary>
    public string SelectedOption { get; init; } = string.Empty;
}

public record TeacherExamSubmitRequest
{
    public int AttemptId { get; init; }
    public IReadOnlyList<TeacherExamAnswerRequest> Answers { get; init; } = [];
}

public class TeacherExamSubmitRequestValidator : AbstractValidator<TeacherExamSubmitRequest>
{
    public TeacherExamSubmitRequestValidator()
    {
        RuleFor(x => x.AttemptId).GreaterThan(0);
        RuleForEach(x => x.Answers).ChildRules(a =>
        {
            a.RuleFor(x => x.SelectedOption)
                .Must(v => v is "A" or "B" or "C" or "D")
                .WithMessage("Each answer must be A, B, C or D.");
        });
    }
}

public record RecordTeacherExamViolationRequest
{
    public int AttemptId { get; init; }

    /// <summary>Short machine reason, e.g. <c>fullscreen-exit</c>, <c>tab-hidden</c>. For the server log only.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Whatever the teacher has answered so far — carried on every violation
    /// report so that if this one trips the limit, the paper can be
    /// auto-submitted with real answers rather than an empty one.
    /// </summary>
    public IReadOnlyList<TeacherExamAnswerRequest> Answers { get; init; } = [];
}

/// <summary>Set only when the violation limit was just reached and the paper was auto-submitted.</summary>
public record TeacherExamViolationResultDto(
    int ViolationCount, int Limit, bool MustSubmit, TeacherExamResultDto? Result);

public record TeacherExamResultDto(
    int AttemptId,
    int ScheduleId,
    string CourseName,
    int TotalQuestions,
    int CorrectCount,
    decimal ScorePercent,
    bool Passed);

/* ==================================================================== service */

public interface ITeacherExamService
{
    /// <summary>Resolves the signed-in teacher's own id from their login email.</summary>
    Task<Result<int>> ResolveOwnTeacherIdAsync(string email, CancellationToken ct = default);

    /// <summary>Every standing assignment, with live due-status — Admin's overview screen.</summary>
    Task<IReadOnlyList<TeacherExamAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default);

    Task<Result<int>> CreateAssignmentAsync(
        TeacherExamAssignmentSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> SetAssignmentActiveAsync(
        int assignmentId, bool isActive, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Schedules a sitting and generates its questions via AI. Notifies the
    /// teacher once the questions are ready.
    /// </summary>
    Task<Result<int>> ScheduleAsync(
        TeacherExamScheduleRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Every sitting — Admin's list, or a specific teacher's own (when
    /// <paramref name="teacherId"/> is set).
    /// </summary>
    Task<IReadOnlyList<TeacherExamScheduleDto>> GetSchedulesAsync(
        int? teacherId, CancellationToken ct = default);

    /// <summary>
    /// Starts (or resumes) an attempt for a sitting this teacher is booked for.
    /// Refused before the scheduled time, and refused once already submitted.
    /// </summary>
    Task<Result<TeacherExamPaperDto>> StartAttemptAsync(
        int scheduleId, int teacherId, CancellationToken ct = default);

    /// <summary>
    /// Scores the attempt, records pass/fail, and notifies the teacher, Admin
    /// and SuperAdmin of the result.
    /// </summary>
    Task<Result<TeacherExamResultDto>> SubmitAttemptAsync(
        TeacherExamSubmitRequest request, int teacherId, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Records one instance of the teacher leaving the exam window, counted
    /// server-side so a reload cannot reset the strike count. Auto-submits
    /// with whatever was answered so far once the limit is reached.
    /// </summary>
    Task<Result<TeacherExamViolationResultDto>> RecordViolationAsync(
        RecordTeacherExamViolationRequest request, int teacherId, string? actor, CancellationToken ct = default);
}
