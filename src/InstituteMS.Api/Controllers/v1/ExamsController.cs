using InstituteMS.Application.Common;
using InstituteMS.Application.Exams;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Exam papers and their question bank — the administrator's side of the
/// examination portal.
/// </summary>
/// <remarks>
/// Receptionist has full control here — same as Admin and Editor, deliberately,
/// including creating, editing and deleting papers, uploading the question bank
/// with its answer key, publishing, and entering practical marks. That is a
/// real, owner-directed choice, not a default: this class went through a phase
/// where Receptionist held only the schedule endpoints, restricted by a
/// per-action <c>[Authorize(Roles = UserRoles.BackOffice)]</c> stacked on top of
/// the class-level policy below — removed now that the restriction itself is
/// gone. (Stacking a class-level and a method-level <c>[Authorize]</c> ANDs the
/// two role sets rather than one loosening the other; worth remembering if a
/// narrower action is ever reintroduced here.)
/// </remarks>
[Route("api/v1/exams")]
[Tags("Exams")]
[Authorize(Roles = UserRoles.MasterData)]
public class ExamsController : ApiControllerBase
{
    private readonly IExamService _exams;

    public ExamsController(IExamService exams) => _exams = exams;

    /// <summary>
    /// Paged list of papers with their question and attempt counts. Open to
    /// Receptionist too — the only door onto this whole page is the papers grid,
    /// and their one job past it is the schedule dialog on a Practical row; every
    /// other action stays hidden client-side and refused here.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ExamPaperDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ExamPaperQuery query, CancellationToken ct)
        => Envelope(await _exams.GetPapersAsync(query, ct));

    [HttpGet("{examId:int}")]
    [ProducesResponseType(typeof(ApiResponse<ExamPaperDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int examId, CancellationToken ct)
        => Envelope(await _exams.GetPaperAsync(examId, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ExamPaperSaveRequest request, CancellationToken ct)
        => Envelope(await _exams.CreatePaperAsync(request, CurrentUserName, ct), "Exam paper created.");

    [HttpPut("{examId:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int examId, [FromBody] ExamPaperSaveRequest request, CancellationToken ct)
        => Envelope(await _exams.UpdatePaperAsync(examId, request, CurrentUserName, ct), "Exam paper updated.");

    /// <summary>Deletes a paper. Refused once anyone has sat it.</summary>
    [HttpDelete("{examId:int}")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int examId, CancellationToken ct)
        => Envelope(await _exams.DeletePaperAsync(examId, ct), "Exam paper deleted.");

    /// <summary>
    /// Publishes or withdraws a paper. Publishing checks that the uploaded question
    /// marks add up to the paper's total.
    /// </summary>
    [HttpPatch("{examId:int}/published")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPublished(
        int examId, [FromQuery] bool isPublished, CancellationToken ct)
        => Envelope(
            await _exams.SetPublishedAsync(examId, isPublished, CurrentUserName, ct),
            isPublished ? "Exam published." : "Exam withdrawn.");

    /// <summary>The question bank, with the answer key.</summary>
    [HttpGet("{examId:int}/questions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamQuestionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Questions(int examId, CancellationToken ct)
        => Envelope(await _exams.GetQuestionsAsync(examId, ct));

    /// <summary>
    /// Uploads the question sheet, replacing whatever the paper had.
    /// </summary>
    /// <remarks>
    /// Columns: Question, Option A, Option B, Option C, Option D, Correct Option,
    /// Marks. Nothing is saved unless every row is valid, so the sheet can be fixed
    /// in one pass rather than one error at a time.
    /// </remarks>
    [HttpPost("{examId:int}/questions/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<ExamUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExamUploadResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadQuestions(
        int examId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Envelope(Result<ExamUploadResultDto>.Failure(
                "Choose an .xlsx file to upload."));
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Envelope(Result<ExamUploadResultDto>.Failure(
                "The question sheet must be an .xlsx workbook. Download the template to start from."));
        }

        await using var stream = file.OpenReadStream();

        return Envelope(
            await _exams.UploadQuestionsAsync(examId, stream, CurrentUserName, ct),
            "Questions uploaded.");
    }

    /// <summary>
    /// The mark sheet for a practical paper: every student on its course, with
    /// whatever score has been entered so far.
    /// </summary>
    [HttpGet("{examId:int}/practical/roster")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PracticalRosterRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PracticalRosterRowDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PracticalRoster(int examId, CancellationToken ct)
        => Envelope(await _exams.GetPracticalRosterAsync(examId, ct));

    /// <summary>
    /// Records or corrects one student's practical mark. A practical is examined in
    /// the room, so its marks are typed in rather than sat online.
    /// </summary>
    [HttpPost("practical/marks")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePracticalMark(
        [FromBody] PracticalMarkRequest request, CancellationToken ct)
        => Envelope(await _exams.SavePracticalMarkAsync(request, CurrentUserName, ct), "Mark saved.");

    /// <summary>
    /// A student's submitted online paper, question by question, read-only —
    /// their answer, whether it was right, and the correct option.
    /// </summary>
    [HttpGet("attempts/{attemptId:int}/review")]
    [ProducesResponseType(typeof(ApiResponse<ExamAttemptReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttemptReview(int attemptId, CancellationToken ct)
        => Envelope(await _exams.GetAttemptReviewAsync(attemptId, ownerEmail: null, ct));

    /// <summary>The uploaded answer script for one student's offline theory paper.</summary>
    [HttpGet("{examId:int}/answer-copies")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamAnswerCopyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnswerCopies(int examId, [FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _exams.GetAnswerCopiesAsync(examId, registrationId, ct));

    /// <summary>
    /// Uploads a student's offline theory answer script — JPEG/PNG photos (one
    /// per page) or a PDF scan. Required before that paper's mark can be entered.
    /// </summary>
    [HttpPost("{examId:int}/answer-copies")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamAnswerCopyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamAnswerCopyDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAnswerCopies(
        int examId, [FromQuery] string registrationId, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        var streams = new List<Stream>();
        try
        {
            var uploads = files.Select(f =>
            {
                var s = f.OpenReadStream();
                streams.Add(s);
                return new AnswerCopyUpload(f.FileName, f.ContentType, f.Length, s);
            }).ToList();

            return Envelope(
                await _exams.UploadAnswerCopiesAsync(examId, registrationId, uploads, CurrentUserName, ct),
                "Answer copy uploaded.");
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }

    /// <summary>One answer copy's file, to view or download.</summary>
    [HttpGet("answer-copies/{copyId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AnswerCopyFile(int copyId, CancellationToken ct)
    {
        var result = await _exams.GetAnswerCopyFileAsync(copyId, ct);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : Envelope(result);
    }

    [HttpDelete("answer-copies/{copyId:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAnswerCopy(int copyId, CancellationToken ct)
        => Envelope(await _exams.DeleteAnswerCopyAsync(copyId, ct), "Answer copy removed.");

    /// <summary>
    /// The paper's scheduling sheet: every student on its course, with the date and
    /// time they sit it and the six-digit code that opens it.
    /// </summary>
    /// <remarks>
    /// The codes are visible here and nowhere else. The invigilator reads a
    /// student's code out at the desk; the student types it beside their own
    /// credentials, and only then does the paper open.
    /// </remarks>
    [HttpGet("{examId:int}/schedule")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamScheduleRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Schedule(int examId, CancellationToken ct)
        => Envelope(await _exams.GetScheduleAsync(examId, ct));

    /// <summary>
    /// Schedules one student and issues their code, or moves a sitting. Open to
    /// Receptionist for a Practical row — the service itself refuses a Practical
    /// date until that student has submitted Theory, so the role grant alone does
    /// not let a Practical get ahead of its Theory.
    /// </summary>
    [HttpPost("schedule")]
    [ProducesResponseType(typeof(ApiResponse<ExamScheduleRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExamScheduleRowDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveSchedule(
        [FromBody] ExamScheduleSaveRequest request, CancellationToken ct)
        => Envelope(await _exams.SaveScheduleAsync(request, CurrentUserName, ct), "Sitting saved.");

    /// <summary>
    /// Schedules a batch at one date and time. Each student still gets their own
    /// code, so one number cannot open the whole room.
    /// </summary>
    [HttpPost("schedule/batch")]
    [ProducesResponseType(typeof(ApiResponse<ExamScheduleBulkResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScheduleBatch(
        [FromBody] ExamScheduleBulkRequest request, CancellationToken ct)
        => Envelope(await _exams.ScheduleBatchAsync(request, CurrentUserName, ct), "Batch scheduled.");

    /* A registration id is "AVI/Raj/4998/26" — it contains slashes, so it cannot be
       a path segment. Carrying it in the query string instead is what every other
       endpoint here already does; as a route parameter both of these returned "that
       student has not been scheduled" for every student at the institute. */

    /// <summary>Issues a fresh code, retiring the one already given out.</summary>
    [HttpPost("{examId:int}/schedule/regenerate-code")]
    [ProducesResponseType(typeof(ApiResponse<ExamScheduleRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegenerateCode(
        int examId, [FromQuery] string registrationId, CancellationToken ct)
        => Envelope(
            await _exams.RegenerateAccessCodeAsync(examId, registrationId, CurrentUserName, ct),
            "New code issued. The previous one no longer works.");

    /// <summary>Withdraws a sitting. Refused once the student has opened the paper.</summary>
    [HttpDelete("{examId:int}/schedule")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveSchedule(
        int examId, [FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _exams.RemoveScheduleAsync(examId, registrationId, ct), "Sitting withdrawn.");

    /// <summary>The blank workbook to fill in, with one example row.</summary>
    [HttpGet("questions/template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult QuestionTemplate()
        => File(
            _exams.BuildQuestionTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "avijan-exam-questions-template.xlsx");
}

/// <summary>
/// The student's side of the examination portal: what they may sit, sitting it,
/// and their results.
/// </summary>
/// <remarks>
/// Student role only, and every call resolves the student from the signed-in
/// email rather than trusting an id from the browser. The answer key is never
/// sent to the client, and the deadline is the server's.
/// </remarks>
[Route("api/v1/my-exams")]
[Tags("Exams")]
[Authorize(Roles = UserRoles.Student)]
public class MyExamsController : ApiControllerBase
{
    private readonly IExamService _exams;

    public MyExamsController(IExamService exams) => _exams = exams;

    /// <summary>Papers this student may sit, with where they stand on each.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableExamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Available(CancellationToken ct)
        => Envelope(await _exams.GetAvailableAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// Opens the paper, or resumes it. Resuming returns the same deadline, so
    /// closing the browser does not restart the clock.
    /// </summary>
    /// <remarks>
    /// Needs the six-digit code the invigilator reads out, as well as the student's
    /// own sign-in, and refuses before the date and time the institute set. The code
    /// is checked on resume too: picking the paper up elsewhere needs it again.
    /// </remarks>
    [HttpPost("{examId:int}/start")]
    [ProducesResponseType(typeof(ApiResponse<ExamSittingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExamSittingDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ExamSittingDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ExamSittingDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(
        int examId, [FromBody] StartExamRequest request, CancellationToken ct)
        => Envelope(await _exams.StartAsync(
            CurrentUserName ?? string.Empty, examId, request.AccessCode, ct));

    /// <summary>
    /// The admit cards this student may print — one per examination they have been
    /// scheduled for.
    /// </summary>
    /// <remarks>
    /// Empty until the institute sets a date. The card carries the date and time of
    /// the term's theory and practical papers, in the Theory / Practical table the
    /// legacy card laid out. It never carries the access code — that is read out at
    /// the desk, not printed at home.
    /// </remarks>
    [HttpGet("admit-cards")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AdmitCardDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdmitCards(CancellationToken ct)
        => Envelope(await _exams.GetAdmitCardsAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// Saves the answers chosen so far. Called as the student works, so the server
    /// can close the paper on time without losing what they had already picked.
    /// </summary>
    [HttpPost("progress")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveProgress(
        [FromBody] SaveExamProgressRequest request, CancellationToken ct)
        => Envelope(await _exams.SaveProgressAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>
    /// Reports one instance of the student leaving the exam window — exiting
    /// fullscreen, switching tabs, or losing focus to another application.
    /// </summary>
    /// <remarks>
    /// Counted against the attempt on the server, not just shown as a warning in
    /// the browser, so the strike count survives a page reload. Once it reaches
    /// the portal's limit the response says so and the client auto-submits.
    /// </remarks>
    [HttpPost("violation")]
    [ProducesResponseType(typeof(ApiResponse<ExamViolationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExamViolationResultDto>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordViolation(
        [FromBody] RecordExamViolationRequest request, CancellationToken ct)
        => Envelope(await _exams.RecordViolationAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>Submits the paper and returns the marked result immediately.</summary>
    [HttpPost("submit")]
    [ProducesResponseType(typeof(ApiResponse<ExamResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExamResultDto>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Submit([FromBody] SubmitExamRequest request, CancellationToken ct)
        => Envelope(await _exams.SubmitAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>Every paper this student has sat, newest first.</summary>
    [HttpGet("results")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamResultDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Results(CancellationToken ct)
        => Envelope(await _exams.GetMyResultsAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// The student's own submitted paper, read-only: each question, their answer
    /// and whether it was right. The correct options are not included — the
    /// same paper is sat by other students.
    /// </summary>
    [HttpGet("results/{attemptId:int}/review")]
    [ProducesResponseType(typeof(ApiResponse<ExamAttemptReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Review(int attemptId, CancellationToken ct)
        => Envelope(await _exams.GetAttemptReviewAsync(attemptId, CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// The student's standing course by course: each term read as one theory plus
    /// one practical, with the average, percentage and grade across them.
    /// </summary>
    /// <remarks>
    /// A term's grade appears only once BOTH its papers are in — the theory sat and
    /// the practical entered by the institute — because a grade on half an
    /// examination would misrepresent it.
    /// </remarks>
    [HttpGet("course-results")]
    [ProducesResponseType(typeof(ApiResponse<StudentExamResultsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CourseResults(CancellationToken ct)
        => Envelope(await _exams.GetMyCourseResultsAsync(CurrentUserName ?? string.Empty, ct));
}
