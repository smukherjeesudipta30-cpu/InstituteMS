using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Exams;

/* ============================================================ admin: papers */

public class ExamPaperQuery : PagedQuery
{
    public bool? IsPublished { get; set; }
    public int? CourseId { get; set; }
}

public record ExamPaperDto(
    int ExamId,
    string Title,
    string? Subject,
    int? CourseId,
    string? CourseName,
    int TotalMarks,
    int? PassMarks,
    int DurationMinutes,
    DateTime? OpensOn,
    DateTime? ClosesOn,
    bool IsPublished,

    /// <summary><c>Theory</c> or <c>Practical</c>.</summary>
    string PaperType,
    /// <summary>Which examination of the course.</summary>
    int TermNumber,

    /// <summary>How many questions have been uploaded. Always 0 for a practical.</summary>
    int QuestionCount,
    /// <summary>Sum of the question marks — must equal <see cref="TotalMarks"/> to publish.</summary>
    int UploadedMarks,
    int AttemptCount,
    DateTime CreatedOn,

    /// <summary>
    /// True when the other half of this examination exists — a theory paper's
    /// practical, or a practical's theory, on the same course and term number.
    /// </summary>
    /// <remarks>
    /// A term needs both papers before a student can be graded on it, so a paper
    /// without its counterpart is an examination that can never complete. Surfaced
    /// on the list because the gap is otherwise invisible until a student is
    /// already waiting on a grade that will not arrive.
    /// </remarks>
    bool HasCounterpart);

public record ExamPaperSaveRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Subject { get; init; }

    /// <summary>
    /// Required. Papers are organised per course, and the pairing rule - one
    /// theory and one practical per term - is scoped to a course.
    /// </summary>
    public int? CourseId { get; init; }

    /// <summary><c>Theory</c> (sat online) or <c>Practical</c> (marked by hand).</summary>
    public string PaperType { get; init; } = "Theory";

    /// <summary>Which examination of this course this paper belongs to. Not capped.</summary>
    public int TermNumber { get; init; } = 1;

    /// <summary>Free — 25, 40, 50, 100 or anything else.</summary>
    public int TotalMarks { get; init; }

    public int? PassMarks { get; init; }

    /// <summary>Ignored for a practical paper, which is not sat online.</summary>
    public int DurationMinutes { get; init; }

    public DateTime? OpensOn { get; init; }
    public DateTime? ClosesOn { get; init; }
}

public class ExamPaperSaveRequestValidator : AbstractValidator<ExamPaperSaveRequest>
{
    public ExamPaperSaveRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Give the paper a title.")
            .MaximumLength(200);

        RuleFor(x => x.Subject).MaximumLength(100);

        // Papers are organised per course; the theory/practical pairing needs it.
        RuleFor(x => x.CourseId)
            .NotNull().WithMessage("Choose the course this paper belongs to.")
            .GreaterThan(0).WithMessage("Choose the course this paper belongs to.");

        RuleFor(x => x.PaperType)
            .Must(t => t is "Theory" or "Practical")
            .WithMessage("Paper type must be Theory or Practical.");

        // Terms are not capped: a course may be examined as many times as the
        // institute chooses. Each term is one theory paper plus one practical.
        RuleFor(x => x.TermNumber)
            .GreaterThanOrEqualTo(1).WithMessage("The term number starts at 1.")
            .LessThanOrEqualTo(50).WithMessage("That term number looks like a typo.");

        RuleFor(x => x.TotalMarks)
            .GreaterThan(0).WithMessage("Total marks must be greater than zero.")
            .LessThanOrEqualTo(1000).WithMessage("Total marks must be 1000 or fewer.");

        // Only a theory paper is sat against a clock.
        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Duration must be at least a minute.")
            .LessThanOrEqualTo(600).WithMessage("Duration must be 600 minutes or fewer.")
            .When(x => x.PaperType == "Theory");

        RuleFor(x => x.PassMarks)
            .GreaterThan(0).WithMessage("Pass marks must be greater than zero.")
            .LessThanOrEqualTo(x => x.TotalMarks)
            .WithMessage("Pass marks cannot exceed the paper's total marks.")
            .When(x => x.PassMarks.HasValue);

        RuleFor(x => x.ClosesOn)
            .GreaterThan(x => x.OpensOn!.Value)
            .WithMessage("The closing time must be after the opening time.")
            .When(x => x.OpensOn.HasValue && x.ClosesOn.HasValue);
    }
}

/* ========================================================= admin: questions */

/// <summary>A question with its answer key — admin eyes only.</summary>
public record ExamQuestionDto(
    int QuestionId,
    int QuestionNo,
    string QuestionText,
    string OptionA,
    string OptionB,
    string? OptionC,
    string? OptionD,
    char CorrectOption,
    int Marks);

/// <summary>What an upload did, and everything it refused and why.</summary>
public record ExamUploadResultDto(
    int ExamId,
    int RowsRead,
    int QuestionsSaved,
    int UploadedMarks,
    int PaperTotalMarks,
    /// <summary>True when the uploaded marks add up to the paper's total.</summary>
    bool MarksBalance,
    IReadOnlyList<string> Errors);

/* ========================================================= student: sitting */

/// <summary>One paper a student may sit, as it appears on their exam list.</summary>
public record AvailableExamDto(
    int ExamId,
    string Title,
    string? Subject,
    string? CourseName,
    int TotalMarks,
    int? PassMarks,
    int DurationMinutes,
    DateTime? OpensOn,
    DateTime? ClosesOn,
    int QuestionCount,

    /// <summary>
    /// Where this student stands: NotScheduled, Scheduled, NotStarted, InProgress,
    /// Submitted, NotOpen or Closed.
    /// </summary>
    string Status,
    DateTime? SubmittedOn,
    int? Score,

    /// <summary>
    /// When the institute has sat this student. Null until they are scheduled, and
    /// the paper will not open before it.
    /// </summary>
    DateTime? ScheduledOn,

    /// <summary>
    /// True once a sitting is set. The paper needs the invigilator's six-digit code
    /// to open, so the student is told to bring their admit card.
    /// </summary>
    bool IsScheduled);

/// <summary>
/// A question as the student sees it. There is deliberately no correct-answer
/// field: the key never reaches the browser, so it cannot be read out of the
/// network tab mid-exam.
/// </summary>
public record ExamQuestionForStudentDto(
    int QuestionId,
    int QuestionNo,
    string QuestionText,
    string OptionA,
    string OptionB,
    string? OptionC,
    string? OptionD,
    int Marks,
    /// <summary>What they picked already, if they are resuming.</summary>
    char? SelectedOption);

/// <summary>The paper in progress, with the server's deadline.</summary>
public record ExamSittingDto(
    int AttemptId,
    int ExamId,
    string Title,
    string? Subject,
    int TotalMarks,
    int DurationMinutes,
    DateTime StartedOn,

    /// <summary>Server-side deadline. The countdown is cosmetic; this decides.</summary>
    DateTime DueOn,
    int SecondsRemaining,

    IReadOnlyList<ExamQuestionForStudentDto> Questions,

    /// <summary>
    /// Carried over from a previous sitting of the same attempt, so resuming
    /// after a dropped connection does not also reset the student's strike
    /// count back to zero.
    /// </summary>
    int ViolationCount);

/// <summary>
/// What the student types to open a paper: the six digits the invigilator reads
/// out at the desk.
/// </summary>
/// <remarks>
/// Deliberately separate from the sign-in. Credentials say who you are; the code
/// says the institute has you sitting here, now — so a paper cannot be opened
/// early, from home, or by someone holding a borrowed password.
/// </remarks>
public record StartExamRequest
{
    public string AccessCode { get; init; } = string.Empty;
}

public class StartExamRequestValidator : AbstractValidator<StartExamRequest>
{
    public StartExamRequestValidator()
    {
        RuleFor(x => x.AccessCode)
            .NotEmpty().WithMessage("Enter the six-digit code the invigilator gives you.")
            .Matches("^[0-9]{6}$").WithMessage("The code is six digits.");
    }
}

public record SubmitExamRequest
{
    public int AttemptId { get; init; }

    /// <summary>Question id to chosen option. Unanswered questions may be omitted.</summary>
    public IReadOnlyDictionary<int, char>? Answers { get; init; }
}

/// <summary>
/// The paper as it stands part-way through, saved as the student works.
/// </summary>
/// <remarks>
/// This exists so the deadline can be enforced on the server without a dropped
/// connection costing a student their paper. Without a saved copy the only way to
/// refuse a late submission is to award nothing, which punishes the student whose
/// browser closed at the last minute exactly as hard as the one still typing.
/// </remarks>
public record SaveExamProgressRequest
{
    public int AttemptId { get; init; }

    public IReadOnlyDictionary<int, char>? Answers { get; init; }
}

/// <summary>
/// Reported by the portal each time the student leaves the exam window —
/// exits fullscreen, switches tabs, or moves focus to another application.
/// </summary>
public record RecordExamViolationRequest
{
    public int AttemptId { get; init; }

    /// <summary>Free text for the log — "fullscreen-exit", "tab-hidden", "window-blur".</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>What the portal is told back, so it knows whether to keep going or auto-submit.</summary>
public record ExamViolationResultDto(int ViolationCount, int Limit, bool MustSubmit);

/// <summary>What the student is shown the moment they submit.</summary>
public record ExamResultDto(
    int AttemptId,
    int ExamId,
    string Title,
    string? Subject,
    int Score,
    int TotalMarks,
    decimal PercentScored,
    int CorrectCount,
    int QuestionCount,
    int? PassMarks,
    /// <summary>Null when the paper has no pass mark set.</summary>
    bool? Passed,
    string Grade,
    DateTime SubmittedOn,
    bool AutoSubmitted,

    /// <summary>Times the student left the exam window during this sitting.</summary>
    int ViolationCount,
    /// <summary>True when the paper was auto-submitted for exceeding the violation limit, rather than for running out of time or the student's own submit.</summary>
    bool Flagged);

/* =============================================== practical marks (admin) */

/// <summary>
/// The practical score an administrator types in. A practical paper is examined
/// in the room, not online, so there is no sitting — only a mark.
/// </summary>
public record PracticalMarkRequest
{
    public int ExamId { get; init; }
    public string RegistrationId { get; init; } = string.Empty;
    public int Score { get; init; }
}

public class PracticalMarkRequestValidator : AbstractValidator<PracticalMarkRequest>
{
    public PracticalMarkRequestValidator()
    {
        RuleFor(x => x.ExamId).GreaterThan(0);

        RuleFor(x => x.RegistrationId)
            .NotEmpty().WithMessage("A student registration id is required.")
            .MaximumLength(15);

        RuleFor(x => x.Score)
            .GreaterThanOrEqualTo(0).WithMessage("Marks cannot be negative.");
    }
}

/// <summary>One row of the practical mark-entry sheet.</summary>
public record PracticalRosterRowDto(
    string RegistrationId,
    string? StudentName,
    int? Score,
    int TotalMarks,
    DateTime? EnteredOn,
    string? EnteredBy,
    // The result's attempt, when there is one — an online sitting's can be
    // opened read-only with GetAttemptReviewAsync.
    int? AttemptId = null,
    // "Portal" (sat online, marked by the portal) or "Entered" (hand-marked).
    string? Source = null,
    // True when this theory paper is scheduled offline for the student, so a
    // mark can only be entered once an answer copy is on file.
    bool IsOfflineTheory = false,
    int AnswerCopyCount = 0);

/* ======================================= reviewing a submitted paper */

/// <summary>One question of a submitted online paper, as the student sat it.</summary>
/// <remarks>
/// <see cref="CorrectOption"/> is filled in for staff only. A paper is reused
/// for other students, so the student's own view shows whether each answer was
/// right but never what the right answer was.
/// </remarks>
public record ExamAttemptReviewItemDto(
    int QuestionNo,
    string QuestionText,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    string? ChosenOption,
    bool IsCorrect,
    int Marks,
    string? CorrectOption);

/// <summary>A submitted online paper, read-only, question by question.</summary>
public record ExamAttemptReviewDto(
    int AttemptId,
    int ExamId,
    string Title,
    string RegistrationId,
    string? StudentName,
    int Score,
    int TotalMarks,
    int CorrectCount,
    int QuestionCount,
    DateTime? SubmittedOn,
    IReadOnlyList<ExamAttemptReviewItemDto> Questions);

/* ========================== offline theory: the uploaded answer script */

/// <summary>One uploaded page or scan of a student's offline answer script.</summary>
public record ExamAnswerCopyDto(
    int Id,
    string FileName,
    string ContentType,
    int SizeBytes,
    string? UploadedBy,
    DateTime UploadedOn);

/// <summary>A file handed to the answer-copy upload, before it is validated.</summary>
public record AnswerCopyUpload(string FileName, string? ContentType, long Length, Stream Content);

/// <summary>An answer copy's bytes, for download.</summary>
public record AnswerCopyFile(string FileName, string ContentType, byte[] Content);

/* ================================ scheduling and the access code (admin) */

/// <summary>
/// One row of a paper's scheduling sheet: a student on the course, and their
/// sitting if one has been set.
/// </summary>
/// <remarks>
/// <see cref="AccessCode"/> is shown to the administrator alone. It is what the
/// invigilator reads out at the desk, and the student types it beside their own
/// credentials to open the paper.
/// </remarks>
public record ExamScheduleRowDto(
    string RegistrationId,
    string? StudentName,
    string? GuardianName,

    int? ScheduleId,
    DateTime? ScheduledOn,
    string? AccessCode,
    DateTime? CodeIssuedOn,
    string? CodeIssuedBy,

    /// <summary>True once the student has opened or sat the paper.</summary>
    bool HasAttempted,
    DateTime? SubmittedOn,

    /// <summary>
    /// Still enrolled. The sheet lists only enrolled students, so this is false
    /// only on someone kept on it because they already have a sitting here.
    /// </summary>
    bool IsActive,

    /// <summary>Has finished the course, and will not come in to sit anything.</summary>
    bool IsComplete,

    /// <summary>
    /// Null on a Theory paper's own sheet. On a Practical's, when this student has
    /// submitted the Theory paper of the same course and term — the Practical date
    /// cannot be set for them until then.
    /// </summary>
    DateTime? TheorySubmittedOn = null,

    /// <summary>
    /// How this student's theory paper is sat — <c>Online</c> or <c>Offline</c>.
    /// Always <c>Online</c> for a practical.
    /// </summary>
    string Mode = "Online",

    /// <summary>
    /// The student's exam fee is set but not fully paid. A warning only: it never
    /// stops the student being scheduled or getting their admit card.
    /// </summary>
    bool ExamFeeUnpaid = false);

/// <summary>Schedules one student, or corrects a sitting already set.</summary>
public record ExamScheduleSaveRequest
{
    public int ExamId { get; init; }
    public string RegistrationId { get; init; } = string.Empty;
    public DateTime ScheduledOn { get; init; }

    /// <summary>
    /// <c>Online</c> (the app, against the question bank) or <c>Offline</c>
    /// (written on paper, marked by hand). Read only for a theory paper; a
    /// practical is always in the room.
    /// </summary>
    public string Mode { get; init; } = "Online";
}

public class ExamScheduleSaveRequestValidator : AbstractValidator<ExamScheduleSaveRequest>
{
    public ExamScheduleSaveRequestValidator()
    {
        RuleFor(x => x.ExamId).GreaterThan(0);

        RuleFor(x => x.RegistrationId)
            .NotEmpty().WithMessage("A student registration id is required.")
            .MaximumLength(50);

        RuleFor(x => x.ScheduledOn)
            .NotEmpty().WithMessage("Set the date and time the student sits the paper.");

        RuleFor(x => x.Mode)
            .Must(m => m is "Online" or "Offline")
            .WithMessage("Mode must be Online or Offline.");
    }
}

/// <summary>
/// Schedules a whole batch at one date and time. Each student still gets their
/// own code — a shared code would be one number the whole room could pass around.
/// </summary>
public record ExamScheduleBulkRequest
{
    public int ExamId { get; init; }
    public DateTime ScheduledOn { get; init; }

    /// <summary>Empty means every active student on the paper's course.</summary>
    public IReadOnlyList<string>? RegistrationIds { get; init; }

    /// <summary>
    /// False leaves students already scheduled untouched, so a second batch does
    /// not move sittings that have already been printed on admit cards.
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

public class ExamScheduleBulkRequestValidator : AbstractValidator<ExamScheduleBulkRequest>
{
    public ExamScheduleBulkRequestValidator()
    {
        RuleFor(x => x.ExamId).GreaterThan(0);

        RuleFor(x => x.ScheduledOn)
            .NotEmpty().WithMessage("Set the date and time the batch sits the paper.");
    }
}

public record ExamScheduleBulkResultDto(
    int Scheduled,
    int SkippedAlreadyScheduled,
    int SkippedAlreadySat,
    int SkippedTheoryNotDone = 0);

/* ================================================== the admit card (student) */

/// <summary>One line of the admit card's Theory / Practical table.</summary>
public record AdmitCardPaperDto(
    string PaperType,
    int? ExamId,
    string? Title,
    DateTime? ScheduledOn,
    int? DurationMinutes);

/// <summary>
/// The card a student prints before an examination — the legacy
/// <c>Views/Admit/PrintAdmit.cshtml</c>, now carrying real dates.
/// </summary>
/// <remarks>
/// One card per examination of a course. It exists only once the institute has
/// scheduled at least one of the term's two papers; before that there is no date
/// to print and the student is told to wait.
/// </remarks>
public record AdmitCardDto(
    string RegistrationId,
    string? StudentName,
    string? GuardianName,
    int? CourseId,
    string? CourseName,
    int TermNumber,

    AdmitCardPaperDto Theory,
    AdmitCardPaperDto Practical,

    DateTime GeneratedOn);

/* ========================================= the student's combined result */

/// <summary>One paper of a course: the theory or the practical of a given term.</summary>
public record TermPaperResultDto(
    int TermNumber,
    string PaperType,
    int? ExamId,
    string? Title,
    int? Score,
    int? TotalMarks,
    DateTime? CompletedOn,
    /// <summary>False while the paper is still to be sat, or still to be marked.</summary>
    bool IsComplete,

    /// <summary>
    /// True when this mark came from the legacy Marks register rather than a
    /// practical paper's own mark sheet. Shown to the student so a mark's origin is
    /// never a mystery, and a reminder to the office that the paper itself is still
    /// unmarked in the portal.
    /// </summary>
    bool FromMarksRegister = false);

/// <summary>One term of one course: its two papers and their combined standing.</summary>
public record TermResultDto(
    int TermNumber,
    TermPaperResultDto Theory,
    TermPaperResultDto Practical,
    int ObtainedMarks,
    int FullMarks,
    decimal PercentScored,
    string? Grade,
    /// <summary>True only once BOTH papers of the term are in.</summary>
    bool IsComplete);

/// <summary>
/// A student's whole examination for one course: every term, read as one theory
/// plus one practical, and the average across them all once the practical marks
/// are in.
/// </summary>
public record CourseExamResultDto(
    int CourseId,
    string? CourseName,

    IReadOnlyList<TermResultDto> Terms,

    int PapersCompleted,
    /// <summary>Two per term that exists - one theory, one practical.</summary>
    int PapersExpected,

    int TotalObtained,
    int TotalFull,

    /// <summary>Average mark per completed paper.</summary>
    decimal AverageMarks,

    decimal PercentScored,
    string Grade,

    /// <summary>
    /// The figures are final only once every paper of every term is in. Until then
    /// this is the standing so far, and the page says as much.
    /// </summary>
    bool IsFinal);

/// <summary>The student's results page: one block per course they are enrolled on.</summary>
public record StudentExamResultsDto(
    string RegistrationId,
    string? StudentName,
    IReadOnlyList<CourseExamResultDto> Courses,
    DateTime GeneratedOn);

/* ================================================================== service */

public interface IExamService
{
    /* admin */
    Task<PagedResult<ExamPaperDto>> GetPapersAsync(ExamPaperQuery query, CancellationToken ct = default);
    Task<Result<ExamPaperDto>> GetPaperAsync(int examId, CancellationToken ct = default);
    Task<Result<int>> CreatePaperAsync(ExamPaperSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<bool>> UpdatePaperAsync(int examId, ExamPaperSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<bool>> DeletePaperAsync(int examId, CancellationToken ct = default);
    Task<Result<bool>> SetPublishedAsync(int examId, bool isPublished, string? actor, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExamQuestionDto>>> GetQuestionsAsync(int examId, CancellationToken ct = default);

    /// <summary>Replaces the paper's questions from an uploaded .xlsx.</summary>
    Task<Result<ExamUploadResultDto>> UploadQuestionsAsync(
        int examId, Stream excel, string? actor, CancellationToken ct = default);

    /// <summary>The blank workbook an administrator fills in.</summary>
    byte[] BuildQuestionTemplate();

    /* admin: practical marks */

    /// <summary>
    /// The students who may be marked on a practical paper, with whatever score
    /// has been entered so far.
    /// </summary>
    Task<Result<IReadOnlyList<PracticalRosterRowDto>>> GetPracticalRosterAsync(
        int examId, CancellationToken ct = default);

    /// <summary>Records or corrects one student's practical mark.</summary>
    Task<Result<bool>> SavePracticalMarkAsync(
        PracticalMarkRequest request, string? actor, CancellationToken ct = default);

    /* admin: scheduling and access codes */

    /// <summary>
    /// The paper's scheduling sheet: every student on its course, with their
    /// sitting and access code if one has been set.
    /// </summary>
    Task<Result<IReadOnlyList<ExamScheduleRowDto>>> GetScheduleAsync(
        int examId, CancellationToken ct = default);

    /// <summary>
    /// Schedules one student and issues their code, or corrects a sitting already
    /// set. Returns the row as the administrator should now read it.
    /// </summary>
    Task<Result<ExamScheduleRowDto>> SaveScheduleAsync(
        ExamScheduleSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Schedules a batch at one date and time, each with their own code.</summary>
    Task<Result<ExamScheduleBulkResultDto>> ScheduleBatchAsync(
        ExamScheduleBulkRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Issues a fresh code, retiring the one already given out.</summary>
    Task<Result<ExamScheduleRowDto>> RegenerateAccessCodeAsync(
        int examId, string registrationId, string? actor, CancellationToken ct = default);

    /// <summary>Withdraws a sitting. Refused once the student has opened the paper.</summary>
    Task<Result<bool>> RemoveScheduleAsync(
        int examId, string registrationId, CancellationToken ct = default);

    /* student */

    /* student */
    Task<Result<IReadOnlyList<AvailableExamDto>>> GetAvailableAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Opens a theory paper. Needs the six-digit code the invigilator reads out —
    /// the student's own credentials are not enough on their own.
    /// </summary>
    Task<Result<ExamSittingDto>> StartAsync(
        string email, int examId, string? accessCode, CancellationToken ct = default);

    /// <summary>
    /// The admit cards this student may print — one per examination they have been
    /// scheduled for. Empty until the institute sets a date.
    /// </summary>
    Task<Result<IReadOnlyList<AdmitCardDto>>> GetAdmitCardsAsync(
        string email, CancellationToken ct = default);
    Task<Result<bool>> SaveProgressAsync(string email, SaveExamProgressRequest request, CancellationToken ct = default);
    Task<Result<ExamViolationResultDto>> RecordViolationAsync(string email, RecordExamViolationRequest request, CancellationToken ct = default);

    Task<Result<ExamResultDto>> SubmitAsync(string email, SubmitExamRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ExamResultDto>>> GetMyResultsAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// The student's combined standing: each term's theory and practical, and the
    /// average, percentage and grade across every paper of each course.
    /// </summary>
    Task<Result<StudentExamResultsDto>> GetMyCourseResultsAsync(
        string email, CancellationToken ct = default);

    /// <summary>
    /// A submitted online paper, question by question, read-only. With an
    /// <paramref name="ownerEmail"/> it is the student's own view — only their
    /// own attempts, and without the correct options; without one it is the
    /// staff view, which shows everything.
    /// </summary>
    Task<Result<ExamAttemptReviewDto>> GetAttemptReviewAsync(
        int attemptId, string? ownerEmail, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExamAnswerCopyDto>>> GetAnswerCopiesAsync(
        int examId, string registrationId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExamAnswerCopyDto>>> UploadAnswerCopiesAsync(
        int examId, string registrationId, IReadOnlyList<AnswerCopyUpload> files,
        string? actor, CancellationToken ct = default);

    Task<Result<AnswerCopyFile>> GetAnswerCopyFileAsync(int copyId, CancellationToken ct = default);

    Task<Result<bool>> DeleteAnswerCopyAsync(int copyId, CancellationToken ct = default);
}
