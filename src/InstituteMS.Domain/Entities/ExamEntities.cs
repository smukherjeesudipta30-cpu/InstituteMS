namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.ExamPaper</c> — one online MCQ paper.</summary>
public class ExamPaper
{
    public int ExamId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The theory subject examined. The practical paper is marked by hand on the
    /// Marks screen; only theory is sat online.
    /// </summary>
    public string? Subject { get; set; }

    public int? CourseId { get; set; }

    /// <summary>Free: 25, 40, 50, 100 or anything else. Equals the sum of the question marks.</summary>
    public int TotalMarks { get; set; }

    public int? PassMarks { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>The window the paper may be sat in. Null on either side means open-ended.</summary>
    public DateTime? OpensOn { get; set; }
    public DateTime? ClosesOn { get; set; }

    /// <summary>Students see the paper only once this is set.</summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// <c>Theory</c> or <c>Practical</c>. Only a theory paper is sat online; a
    /// practical is marked by the administrator, who enters the score.
    /// </summary>
    public string PaperType { get; set; } = "Theory";

    /// <summary>Which examination of the course this paper belongs to. Not capped.</summary>
    public int TermNumber { get; set; } = 1;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public Course? Course { get; set; }
    public ICollection<ExamQuestion> Questions { get; set; } = [];
}

/// <summary>
/// Maps to <c>dbo.ExamQuestion</c> — the single table holding every piece of
/// question data.
/// </summary>
/// <remarks>
/// Deliberately denormalised: options, answer key and mark all live on the
/// question row, the same shape <c>dbo.Marks</c> uses for its four subject slots.
/// Migrating a question bank is then a single-table copy.
/// </remarks>
public class ExamQuestion
{
    public int QuestionId { get; set; }
    public int ExamId { get; set; }

    /// <summary>Order shown to the student, and the row number in the upload.</summary>
    public int QuestionNo { get; set; }

    public string QuestionText { get; set; } = string.Empty;

    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;

    /// <summary>C and D are optional — a paper may be true/false.</summary>
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }

    /// <summary>
    /// 'A' to 'D'. Never leaves the server while an exam is in progress: the DTO
    /// the student receives has no answer field at all.
    /// </summary>
    public char CorrectOption { get; set; }

    public int Marks { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public ExamPaper? Paper { get; set; }
}

/// <summary>Maps to <c>dbo.ExamAttempt</c> — one student's sitting of one paper.</summary>
public class ExamAttempt
{
    public int AttemptId { get; set; }
    public int ExamId { get; set; }
    public string StudentRegistrationId { get; set; } = string.Empty;

    public DateTime StartedOn { get; set; }

    /// <summary>
    /// Start + duration, fixed when the attempt opens. The deadline is decided by
    /// the server so reloading the page cannot buy more time.
    /// </summary>
    public DateTime DueOn { get; set; }

    public DateTime? SubmittedOn { get; set; }

    /// <summary>
    /// The student's choices as <c>"12:A,13:C"</c>. Held here rather than in a
    /// fourth table so an attempt is one row to migrate.
    /// </summary>
    public string? Answers { get; set; }

    /// <summary>
    /// Scored once, at submission. Stored rather than recomputed so a result stays
    /// correct even if the question bank is edited afterwards.
    /// </summary>
    public int? Score { get; set; }

    public int? TotalMarks { get; set; }
    public int? CorrectCount { get; set; }
    public int? QuestionCount { get; set; }

    /// <summary>True when the server closed the attempt on time.</summary>
    public bool AutoSubmitted { get; set; }

    /// <summary>
    /// Times the student left the exam window during this sitting — exited
    /// fullscreen, switched tabs, or moved focus elsewhere. Incremented
    /// server-side as the portal reports each one, so reloading the page
    /// cannot reset it.
    /// </summary>
    public int ViolationCount { get; set; }

    /// <summary>Set once <see cref="ViolationCount"/> reaches the portal's limit, at which point the paper is auto-submitted.</summary>
    public bool Flagged { get; set; }

    /// <summary>
    /// Generated once, the first time this attempt is opened, and used to give this
    /// student their own shuffled question order — stable across a resumed sitting,
    /// since the same seed always produces the same order. Null on every attempt
    /// that predates this column, which keeps the original <c>QuestionNo</c> order.
    /// </summary>
    public int? ShuffleSeed { get; set; }

    /// <summary><c>Portal</c> if sat online, <c>Entered</c> if typed in by an administrator.</summary>
    public string Source { get; set; } = "Portal";

    /// <summary>Who entered a practical score.</summary>
    public string? EnteredBy { get; set; }

    /// <summary>
    /// Course, term and paper type are copied from the paper so the
    /// "four papers per course" rule can be a single unique index.
    /// </summary>
    public int? CourseId { get; set; }

    public int? TermNumber { get; set; }

    public string? PaperType { get; set; }

    public DateTime CreatedOn { get; set; }

    public ExamPaper? Paper { get; set; }
}

/// <summary>
/// Maps to <c>dbo.ExamSchedule</c> — when one student sits one paper, and the
/// code that opens it.
/// </summary>
/// <remarks>
/// The institute sits students in batches, so the date and time are set per
/// student rather than once for the whole course. Two things hang off this row:
/// the admit card, which a student can only download once scheduled, and the
/// six-digit <see cref="AccessCode"/>, which the invigilator reads out and the
/// student types alongside their own credentials. Credentials alone will not open
/// a theory paper.
/// </remarks>
public class ExamSchedule
{
    public int ScheduleId { get; set; }
    public int ExamId { get; set; }

    public string StudentRegistrationId { get; set; } = string.Empty;

    /// <summary>Printed on the admit card. The paper will not open before it.</summary>
    public DateTime ScheduledOn { get; set; }

    /// <summary>
    /// Six digits, held as text so a leading zero survives. Null on a practical,
    /// which is examined in the room and needs no code.
    /// </summary>
    public string? AccessCode { get; set; }

    public DateTime? CodeIssuedOn { get; set; }
    public string? CodeIssuedBy { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public ExamPaper? Paper { get; set; }
}
