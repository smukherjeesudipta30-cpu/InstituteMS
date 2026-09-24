namespace InstituteMS.Domain.Entities;

/// <summary>
/// One course a teacher must periodically be re-tested on — Admin's standing
/// assignment, not a single sitting. Maps to <c>dbo.TeacherExamAssignment</c>.
/// </summary>
/// <remarks>
/// There is no stored "next due date": it is always worked out live from this
/// assignment's most recent completed <see cref="TeacherExamSchedule"/> — three
/// months after a pass, immediately after a fail — so it can never drift out of
/// sync with what actually happened. See <c>TeacherExamService.ComputeDueStateAsync</c>.
/// </remarks>
public class TeacherExamAssignment
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public int CourseId { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public Teacher? Teacher { get; set; }
    public Course? Course { get; set; }
    public ICollection<TeacherExamSchedule> Schedules { get; set; } = [];
}

/// <summary>
/// One sitting: the date and time Admin has set for a teacher to take their
/// assigned course's exam. Maps to <c>dbo.TeacherExamSchedule</c>.
/// </summary>
public class TeacherExamSchedule
{
    public int Id { get; set; }
    public int TeacherExamAssignmentId { get; set; }
    public DateTime ScheduledOn { get; set; }

    /// <summary>
    /// AI-generated notes for the teacher to prepare from before the sitting —
    /// generated alongside the questions, best-effort: a failure here never
    /// blocks scheduling, since the questions are what actually matters.
    /// </summary>
    public string? StudyGuide { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public TeacherExamAssignment? Assignment { get; set; }
    public ICollection<TeacherExamQuestion> Questions { get; set; } = [];
    public TeacherExamAttempt? Attempt { get; set; }
}

/// <summary>
/// One AI-generated multiple-choice question for a single sitting — generated
/// fresh each time a sitting is scheduled, not drawn from a reusable bank, so
/// the same questions never repeat across a teacher's retakes. Maps to
/// <c>dbo.TeacherExamQuestion</c>.
/// </summary>
public class TeacherExamQuestion
{
    public int Id { get; set; }
    public int TeacherExamScheduleId { get; set; }
    public int OrderNo { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string OptionC { get; set; } = string.Empty;
    public string OptionD { get; set; } = string.Empty;

    /// <summary><c>A</c>, <c>B</c>, <c>C</c> or <c>D</c> — never sent to the teacher before submission.</summary>
    public string CorrectOption { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public TeacherExamSchedule? Schedule { get; set; }
}

/// <summary>
/// The teacher's one attempt at a sitting — one row per <see cref="TeacherExamSchedule"/>,
/// created the moment they start it and scored the moment they submit. Maps to
/// <c>dbo.TeacherExamAttempt</c>.
/// </summary>
public class TeacherExamAttempt
{
    public int Id { get; set; }
    public int TeacherExamScheduleId { get; set; }
    public DateTime StartedOn { get; set; }
    public DateTime? SubmittedOn { get; set; }

    /// <summary>Null until submitted.</summary>
    public decimal? ScorePercent { get; set; }

    /// <summary>Null until submitted. True at 50% or above.</summary>
    public bool? Passed { get; set; }

    /// <summary>
    /// Times the teacher left the exam window — leaving full screen, switching
    /// tabs, minimising — counted here rather than only shown as a client-side
    /// warning, so a reload cannot reset the count. At the limit the paper is
    /// auto-submitted with whatever was answered so far.
    /// </summary>
    public int ViolationCount { get; set; }

    public TeacherExamSchedule? Schedule { get; set; }
    public ICollection<TeacherExamAnswer> Answers { get; set; } = [];
}

/// <summary>One question's answer within an attempt. Maps to <c>dbo.TeacherExamAnswer</c>.</summary>
public class TeacherExamAnswer
{
    public int Id { get; set; }
    public int TeacherExamAttemptId { get; set; }
    public int TeacherExamQuestionId { get; set; }

    /// <summary><c>A</c>, <c>B</c>, <c>C</c>, <c>D</c>, or null if left unanswered.</summary>
    public string? SelectedOption { get; set; }

    public bool IsCorrect { get; set; }

    public TeacherExamAttempt? Attempt { get; set; }
    public TeacherExamQuestion? Question { get; set; }
}
