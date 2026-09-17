using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using InstituteMS.Application.Common;
using InstituteMS.Application.Exams;
using InstituteMS.Application.Reports;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The online MCQ examination portal: papers and their question bank on the admin
/// side, and sitting a paper on the student side.
///
/// <para>Three rules shape the whole design:</para>
/// <list type="number">
///   <item>
///     <b>The answer key never leaves the server.</b>
///     <see cref="ExamQuestionForStudentDto"/> has no correct-answer field at all,
///     so it cannot be read out of the browser's network tab mid-exam. Marking
///     happens here.
///   </item>
///   <item>
///     <b>The clock is the server's.</b> The deadline is fixed when the attempt
///     opens and stored on the row, so reloading the page, or editing the countdown
///     in the console, buys nothing. A late submission is still marked, but only on
///     the answers that arrived — and it is flagged.
///   </item>
///   <item>
///     <b>A result is stored, not recomputed.</b> Score, total and counts are
///     written at submission, so editing the question bank afterwards cannot
///     silently change a mark a student has already been told.
///   </item>
/// </list>
/// </summary>
public class ExamService : IExamService
{
    /// <summary>Grace given to a submission that arrives just after the deadline.</summary>
    private static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Strikes allowed before the portal auto-submits a paper for leaving the exam
    /// window (exiting fullscreen, switching tabs, losing focus). Three rather than
    /// one, because a stray Alt-Tab or the fullscreen prompt itself firing a
    /// fullscreenchange event is realistic and should not cost a genuine student
    /// their paper on the first accidental brush against it.
    /// </summary>
    private const int ViolationLimit = 3;

    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly ILogger<ExamService> _logger;

    public ExamService(AppDbContext db, IReportService reports, ILogger<ExamService> logger)
    {
        _db = db;
        _reports = reports;
        _logger = logger;
    }

    /* ====================================================== admin: papers */

    public async Task<PagedResult<ExamPaperDto>> GetPapersAsync(
        ExamPaperQuery query, CancellationToken ct = default)
    {
        var q = _db.ExamPapers.AsNoTracking();

        if (query.IsPublished.HasValue)
            q = q.Where(p => p.IsPublished == query.IsPublished);

        if (query.CourseId is > 0)
            q = q.Where(p => p.CourseId == query.CourseId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p => EF.Functions.Like(p.Title, $"%{term}%")
                             || (p.Subject != null && EF.Functions.Like(p.Subject, $"%{term}%")));
        }

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(p => p.ExamId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ExamPaperDto(
                p.ExamId, p.Title, p.Subject, p.CourseId,
                p.Course != null ? p.Course.Name : null,
                p.TotalMarks, p.PassMarks, p.DurationMinutes,
                p.OpensOn, p.ClosesOn, p.IsPublished,
                p.PaperType, p.TermNumber,
                _db.ExamQuestions.Count(x => x.ExamId == p.ExamId),
                _db.ExamQuestions.Where(x => x.ExamId == p.ExamId).Sum(x => (int?)x.Marks) ?? 0,
                _db.ExamAttempts.Count(a => a.ExamId == p.ExamId),
                p.CreatedOn,
                // The other half of the same examination: same course, same term,
                // the opposite paper type.
                _db.ExamPapers.Any(o => o.ExamId != p.ExamId
                                        && o.CourseId == p.CourseId
                                        && o.TermNumber == p.TermNumber
                                        && o.PaperType != p.PaperType)))
            .ToListAsync(ct);

        return PagedResult<ExamPaperDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<ExamPaperDto>> GetPaperAsync(int examId, CancellationToken ct = default)
    {
        var result = await GetPapersAsync(
            new ExamPaperQuery { Page = 1, PageSize = 1000 }, ct);

        var paper = result.Items.FirstOrDefault(p => p.ExamId == examId);

        return paper is null
            ? Result<ExamPaperDto>.Failure("That exam paper no longer exists.", ResultStatus.NotFound)
            : Result<ExamPaperDto>.Success(paper);
    }

    public async Task<Result<int>> CreatePaperAsync(
        ExamPaperSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.CourseId is > 0
            && !await _db.Courses.AnyAsync(c => c.Cid == request.CourseId, ct))
        {
            return Result<int>.Failure("That course no longer exists.");
        }

        var paper = new ExamPaper
        {
            Title = request.Title.Trim(),
            Subject = request.Subject?.Trim(),
            CourseId = request.CourseId,
            PaperType = request.PaperType,
            TermNumber = request.TermNumber,
            TotalMarks = request.TotalMarks,
            PassMarks = request.PassMarks,
            // A practical is not sat against a clock; store a nominal duration
            // rather than leave the shared column meaningless.
            DurationMinutes = request.PaperType == "Practical" ? 1 : request.DurationMinutes,
            OpensOn = request.OpensOn,
            ClosesOn = request.ClosesOn,
            IsPublished = false,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.ExamPapers.Add(paper);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(paper.ExamId);
    }

    public async Task<Result<bool>> UpdatePaperAsync(
        int examId, ExamPaperSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<bool>.Failure("That exam paper no longer exists.", ResultStatus.NotFound);

        // Changing the marks or the duration under a student who is mid-exam would
        // move the goalposts on a sitting already in progress.
        if (await HasLiveAttemptAsync(examId, ct))
        {
            return Result<bool>.Failure(
                "A student is sitting this paper right now. Wait until they submit before editing it.",
                ResultStatus.Conflict);
        }

        paper.Title = request.Title.Trim();
        paper.Subject = request.Subject?.Trim();
        paper.CourseId = request.CourseId;
        paper.PaperType = request.PaperType;
        paper.TermNumber = request.TermNumber;
        paper.TotalMarks = request.TotalMarks;
        paper.PassMarks = request.PassMarks;
        paper.DurationMinutes = request.PaperType == "Practical" ? 1 : request.DurationMinutes;
        paper.OpensOn = request.OpensOn;
        paper.ClosesOn = request.ClosesOn;
        paper.ModifiedBy = actor;
        paper.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeletePaperAsync(int examId, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<bool>.Failure("That exam paper no longer exists.", ResultStatus.NotFound);

        // A sat paper is the evidence behind a student's result, so it is never
        // removed once anyone has taken it.
        var attempts = await _db.ExamAttempts.CountAsync(a => a.ExamId == examId, ct);

        if (attempts > 0)
        {
            return Result<bool>.Failure(
                $"{attempts} student(s) have already sat this paper, so it cannot be deleted. " +
                "Unpublish it instead to take it out of circulation.",
                ResultStatus.Conflict);
        }

        _db.ExamPapers.Remove(paper);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetPublishedAsync(
        int examId, bool isPublished, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<bool>.Failure("That exam paper no longer exists.", ResultStatus.NotFound);

        // A practical carries no questions — it is examined in the room — so the
        // question checks below apply to a theory paper only.
        if (isPublished && string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase))
        {
            var questions = await _db.ExamQuestions.Where(q => q.ExamId == examId).ToListAsync(ct);

            if (questions.Count == 0)
                return Result<bool>.Failure("Upload the questions before publishing the paper.");

            // The paper says it is out of N; the questions must actually add to N,
            // or students would be marked against a total nobody agreed.
            var uploaded = questions.Sum(q => q.Marks);

            if (uploaded != paper.TotalMarks)
            {
                return Result<bool>.Failure(
                    $"The questions add up to {uploaded} marks but the paper is set to {paper.TotalMarks}. " +
                    "Fix the marks in the sheet, or change the paper's total, then publish.");
            }
        }

        paper.IsPublished = isPublished;
        paper.ModifiedBy = actor;
        paper.ModifiedOn = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /* =================================================== admin: questions */

    public async Task<Result<IReadOnlyList<ExamQuestionDto>>> GetQuestionsAsync(
        int examId, CancellationToken ct = default)
    {
        if (!await _db.ExamPapers.AnyAsync(p => p.ExamId == examId, ct))
            return Result<IReadOnlyList<ExamQuestionDto>>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        var questions = await _db.ExamQuestions
            .AsNoTracking()
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.QuestionNo)
            .Select(q => new ExamQuestionDto(
                q.QuestionId, q.QuestionNo, q.QuestionText,
                q.OptionA, q.OptionB, q.OptionC, q.OptionD,
                q.CorrectOption, q.Marks))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ExamQuestionDto>>.Success(questions);
    }

    /// <summary>
    /// Reads an uploaded .xlsx and replaces the paper's questions with what it
    /// contains. All or nothing: if any row is bad, nothing is saved and every
    /// problem is reported at once, so the sheet can be fixed in a single pass
    /// rather than one error per upload.
    /// </summary>
    public async Task<Result<ExamUploadResultDto>> UploadQuestionsAsync(
        int examId, Stream excel, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<ExamUploadResultDto>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        if (await HasLiveAttemptAsync(examId, ct))
        {
            return Result<ExamUploadResultDto>.Failure(
                "A student is sitting this paper right now. Wait until they submit before changing the questions.",
                ResultStatus.Conflict);
        }

        if (await _db.ExamAttempts.AnyAsync(a => a.ExamId == examId, ct))
        {
            return Result<ExamUploadResultDto>.Failure(
                "This paper has already been sat, so its questions cannot be replaced. " +
                "Create a new paper instead — replacing them would invalidate results already given.",
                ResultStatus.Conflict);
        }

        List<ExamQuestion> parsed;
        List<string> errors;
        int rowsRead;

        try
        {
            (parsed, errors, rowsRead) = ParseWorkbook(excel, examId, actor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The operator gets plain advice; the cause belongs in the log, or a
            // malformed sheet is indistinguishable from a bug in here.
            _logger.LogWarning(ex, "Could not read the question sheet for exam {ExamId}.", examId);

            return Result<ExamUploadResultDto>.Failure(
                "That file could not be read as an Excel workbook (.xlsx). " +
                "Download the template and fill that in.");
        }

        if (errors.Count > 0)
        {
            return Result<ExamUploadResultDto>.Success(new ExamUploadResultDto(
                examId, rowsRead, 0, 0, paper.TotalMarks, false, errors));
        }

        if (parsed.Count == 0)
        {
            return Result<ExamUploadResultDto>.Failure(
                "The sheet has no question rows under the header.");
        }

        var existing = await _db.ExamQuestions.Where(q => q.ExamId == examId).ToListAsync(ct);
        _db.ExamQuestions.RemoveRange(existing);
        _db.ExamQuestions.AddRange(parsed);

        await _db.SaveChangesAsync(ct);

        var uploadedMarks = parsed.Sum(q => q.Marks);

        return Result<ExamUploadResultDto>.Success(new ExamUploadResultDto(
            examId,
            rowsRead,
            parsed.Count,
            uploadedMarks,
            paper.TotalMarks,
            uploadedMarks == paper.TotalMarks,
            []));
    }

    /// <summary>
    /// Columns: Question, Option A, Option B, Option C, Option D, Correct Option,
    /// Marks. The header row is skipped, blank rows are ignored, and every problem
    /// is collected rather than thrown on the first one.
    /// </summary>
    private static (List<ExamQuestion> Questions, List<string> Errors, int RowsRead) ParseWorkbook(
        Stream excel, int examId, string? actor)
    {
        using var workbook = new XLWorkbook(excel);
        var sheet = workbook.Worksheets.First();

        var questions = new List<ExamQuestion>();
        var errors = new List<string>();
        var rowsRead = 0;
        var questionNo = 0;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var text = Cell(row, 1);
            var a = Cell(row, 2);
            var b = Cell(row, 3);
            var c = Cell(row, 4);
            var d = Cell(row, 5);
            var correct = Cell(row, 6).ToUpperInvariant();
            var marksText = Cell(row, 7);

            // A completely blank row is spacing, not a mistake.
            if (string.IsNullOrWhiteSpace(text)
                && string.IsNullOrWhiteSpace(a)
                && string.IsNullOrWhiteSpace(b))
            {
                continue;
            }

            rowsRead++;
            var line = row.RowNumber();

            if (string.IsNullOrWhiteSpace(text))
                errors.Add($"Row {line}: the question is blank.");

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                errors.Add($"Row {line}: options A and B are both required.");

            if (correct.Length != 1 || !"ABCD".Contains(correct))
            {
                errors.Add($"Row {line}: correct option must be A, B, C or D — found '{Cell(row, 6)}'.");
            }
            else
            {
                // Pointing the key at an option that was left blank marks every
                // student wrong, silently. Catch it here.
                var target = correct switch
                {
                    "A" => a,
                    "B" => b,
                    "C" => c,
                    _ => d
                };

                if (string.IsNullOrWhiteSpace(target))
                    errors.Add($"Row {line}: the correct option is {correct}, but option {correct} is blank.");
            }

            var marks = 1;

            if (!string.IsNullOrWhiteSpace(marksText))
            {
                if (!int.TryParse(marksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out marks)
                    || marks <= 0)
                {
                    errors.Add($"Row {line}: marks must be a whole number above zero — found '{marksText}'.");
                    marks = 1;
                }
            }

            if (errors.Count > 0)
                continue;

            questions.Add(new ExamQuestion
            {
                ExamId = examId,
                QuestionNo = ++questionNo,
                QuestionText = text.Trim(),
                OptionA = Trim(a, 500)!,
                OptionB = Trim(b, 500)!,
                OptionC = Trim(c, 500),
                OptionD = Trim(d, 500),
                CorrectOption = correct[0],
                Marks = marks,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });
        }

        return (questions, errors, rowsRead);

        static string Cell(IXLRow row, int column) =>
            row.Cell(column).GetString().Trim();

        static string? Trim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length > max ? trimmed[..max] : trimmed;
        }
    }

    public byte[] BuildQuestionTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Questions");

        string[] headers =
        [
            "Question", "Option A", "Option B", "Option C", "Option D", "Correct Option", "Marks"
        ];

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // One filled row, so the expected shape is obvious without a manual.
        sheet.Cell(2, 1).Value = "Which keyword declares a constant in C#?";
        sheet.Cell(2, 2).Value = "var";
        sheet.Cell(2, 3).Value = "const";
        sheet.Cell(2, 4).Value = "static";
        sheet.Cell(2, 5).Value = "readonly";
        sheet.Cell(2, 6).Value = "B";
        sheet.Cell(2, 7).Value = 2;

        sheet.Column(1).Width = 60;
        for (var i = 2; i <= 5; i++) sheet.Column(i).Width = 22;
        sheet.Column(6).Width = 16;
        sheet.Column(7).Width = 10;
        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /* ================================================== student: sitting */

    public async Task<Result<IReadOnlyList<AvailableExamDto>>> GetAvailableAsync(
        string email, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<IReadOnlyList<AvailableExamDto>>.Failure(own.Error!, own.Status);

        var regId = own.Value!;
        var now = DateTime.Now;

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == regId)
            .Select(s => new { s.CourseId })
            .FirstOrDefaultAsync(ct);

        // A paper tied to a course is only for students on that course; a paper with
        // no course is open to everyone.
        var papers = await _db.ExamPapers
            .AsNoTracking()
            // Only theory papers appear here: a practical is examined in the room
            // and its mark typed in, so there is nothing for the student to sit.
            .Where(p => p.IsPublished
                        && p.PaperType == "Theory"
                        && (p.CourseId == null || p.CourseId == student!.CourseId))
            .OrderByDescending(p => p.ExamId)
            .Select(p => new
            {
                p.ExamId, p.Title, p.Subject, p.TotalMarks, p.PassMarks,
                p.DurationMinutes, p.OpensOn, p.ClosesOn,
                CourseName = p.Course != null ? p.Course.Name : null,
                QuestionCount = _db.ExamQuestions.Count(q => q.ExamId == p.ExamId)
            })
            .ToListAsync(ct);

        var examIds = papers.Select(p => p.ExamId).ToList();

        var attempts = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.StudentRegistrationId == regId && examIds.Contains(a.ExamId))
            .Select(a => new { a.ExamId, a.SubmittedOn, a.Score, a.DueOn })
            .ToListAsync(ct);

        // A paper opens only for the students the institute has actually sat, and
        // only from the time it sat them. The access code is never selected here —
        // it is the administrator's to read out, not the student's to fetch.
        var schedules = await _db.ExamSchedules
            .AsNoTracking()
            .Where(s => s.StudentRegistrationId == regId && examIds.Contains(s.ExamId))
            .Select(s => new { s.ExamId, s.ScheduledOn })
            .ToListAsync(ct);

        var items = papers
            // A paper with no questions is not sittable, however it is configured.
            .Where(p => p.QuestionCount > 0)
            .Select(p =>
            {
                var attempt = attempts.FirstOrDefault(a => a.ExamId == p.ExamId);
                var schedule = schedules.FirstOrDefault(s => s.ExamId == p.ExamId);

                var status = attempt is not null
                    ? attempt.SubmittedOn is not null ? "Submitted" : "InProgress"
                    : schedule is null ? "NotScheduled"
                    : p.OpensOn is not null && now < p.OpensOn ? "NotOpen"
                    : p.ClosesOn is not null && now > p.ClosesOn ? "Closed"
                    : now < schedule.ScheduledOn ? "Scheduled"
                    : "NotStarted";

                return new AvailableExamDto(
                    p.ExamId, p.Title, p.Subject, p.CourseName,
                    p.TotalMarks, p.PassMarks, p.DurationMinutes,
                    p.OpensOn, p.ClosesOn, p.QuestionCount,
                    status, attempt?.SubmittedOn, attempt?.Score,
                    schedule?.ScheduledOn, schedule is not null);
            })
            .ToList();

        return Result<IReadOnlyList<AvailableExamDto>>.Success(items);
    }

    /// <summary>
    /// Opens or resumes a sitting. Resuming returns the same attempt and the same
    /// deadline, so closing the browser is not a way to restart the clock.
    /// </summary>
    public async Task<Result<ExamSittingDto>> StartAsync(
        string email, int examId, string? accessCode, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<ExamSittingDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;
        var now = DateTime.Now;

        var paper = await _db.ExamPapers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null || !paper.IsPublished)
            return Result<ExamSittingDto>.Failure("That exam is not available.", ResultStatus.NotFound);

        if (paper.OpensOn is not null && now < paper.OpensOn)
            return Result<ExamSittingDto>.Failure(
                $"This exam opens on {paper.OpensOn:d MMM yyyy, h:mm tt}.");

        if (paper.ClosesOn is not null && now > paper.ClosesOn)
            return Result<ExamSittingDto>.Failure(
                $"This exam closed on {paper.ClosesOn:d MMM yyyy} at {paper.ClosesOn:h:mm tt}. " +
                "Speak to the institute — they can reopen it.");

        var attempt = await _db.ExamAttempts
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.StudentRegistrationId == regId, ct);

        if (attempt?.SubmittedOn is not null)
            return Result<ExamSittingDto>.Failure(
                "You have already sat this exam. Your result is on the results page.",
                ResultStatus.Conflict);

        // A practical is examined in the room; there is nothing to sit online.
        if (!string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ExamSittingDto>.Failure(
                "This is a practical paper. It is marked by the institute, not sat online.");
        }

        /* ---- the two gates the sign-in alone does not clear ----------------
           A student may only sit a paper the institute has sat THEM for, at or
           after the time it set, and only with the code the invigilator reads
           out. Both are checked on resume as well as on first open: if someone
           walks away mid-paper, picking it up elsewhere needs the code again. */

        var schedule = await _db.ExamSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.StudentRegistrationId == regId, ct);

        if (schedule is null)
        {
            return Result<ExamSittingDto>.Failure(
                "You have not been scheduled for this exam yet. The institute sets your " +
                "date and time, and gives you an admit card once it has.",
                ResultStatus.Forbidden);
        }

        if (now < schedule.ScheduledOn)
        {
            return Result<ExamSittingDto>.Failure(
                $"Your sitting is on {schedule.ScheduledOn:d MMM yyyy} at " +
                $"{schedule.ScheduledOn:h:mm tt}. The paper opens then.");
        }

        if (string.IsNullOrWhiteSpace(schedule.AccessCode))
        {
            return Result<ExamSittingDto>.Failure(
                "No access code has been issued for your sitting. Ask the invigilator.",
                ResultStatus.Forbidden);
        }

        // Fixed-length, ordinal, and no early exit on the first differing digit —
        // a six-digit secret is small enough that timing should not narrow it.
        if (!FixedTimeEquals(schedule.AccessCode, accessCode?.Trim()))
        {
            return Result<ExamSittingDto>.Failure(
                "That code is not right. Check the six digits the invigilator gave you.",
                ResultStatus.Forbidden);
        }

        if (attempt is null)
        {
            // The rule is four papers per COURSE — (theory + practical) x 2 terms.
            // The unique index enforces it, but catching it here explains why.
            var alreadyThisTerm = await _db.ExamAttempts.AnyAsync(
                a => a.StudentRegistrationId == regId
                     && a.CourseId == paper.CourseId
                     && a.TermNumber == paper.TermNumber
                     && a.PaperType == "Theory", ct);

            if (alreadyThisTerm)
            {
                return Result<ExamSittingDto>.Failure(
                    $"You have already sat the term {paper.TermNumber} theory paper for this course. " +
                    "Each course allows one theory and one practical paper per term.",
                    ResultStatus.Conflict);
            }

            attempt = new ExamAttempt
            {
                ExamId = examId,
                StudentRegistrationId = regId,
                CourseId = paper.CourseId,
                TermNumber = paper.TermNumber,
                PaperType = paper.PaperType,
                Source = "Portal",
                StartedOn = now,
                DueOn = now.AddMinutes(paper.DurationMinutes),
                CreatedOn = now,

                // Two students of the same course sitting this paper at the same
                // time would otherwise see the identical QuestionNo order — an easy
                // paper to copy off the next desk. Rolled once here and never
                // reassigned, so resuming shows the same order again rather than
                // reshuffling mid-sitting.
                ShuffleSeed = Random.Shared.Next()
            };

            _db.ExamAttempts.Add(attempt);
            await _db.SaveChangesAsync(ct);
        }

        var questions = await _db.ExamQuestions
            .AsNoTracking()
            .Where(q => q.ExamId == examId)
            .ToListAsync(ct);

        // Every attempt after this feature shipped carries a seed and gets its own
        // order; an attempt already in progress when it shipped (ShuffleSeed null)
        // keeps the plain QuestionNo order it was already sat with.
        questions = attempt.ShuffleSeed is int seed
            ? questions.OrderBy(q => ShuffleKey(seed, q.QuestionId)).ToList()
            : questions.OrderBy(q => q.QuestionNo).ToList();

        var saved = ParseAnswers(attempt.Answers);

        // Numbered 1..N in THIS student's own order, not the bank's QuestionNo —
        // otherwise a shuffled sitting would show "Q7, Q2, Q15…", which gives the
        // shuffle away and is a worse read than an ordinary paper besides.
        var forStudent = questions
            .Select((q, index) => new ExamQuestionForStudentDto(
                q.QuestionId, index + 1, q.QuestionText,
                q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.Marks,
                saved.TryGetValue(q.QuestionId, out var picked) ? picked : null))
            .ToList();

        var remaining = (int)Math.Max(0, (attempt.DueOn - now).TotalSeconds);

        return Result<ExamSittingDto>.Success(new ExamSittingDto(
            attempt.AttemptId, paper.ExamId, paper.Title, paper.Subject,
            paper.TotalMarks, paper.DurationMinutes,
            attempt.StartedOn, attempt.DueOn, remaining,
            forStudent, attempt.ViolationCount));
    }

    /// <summary>
    /// Saves the paper part-way through, so the deadline can be enforced without a
    /// dropped connection costing the student their answers.
    /// </summary>
    /// <remarks>
    /// Nothing is marked here and no score is written — this only records what has
    /// been chosen so far. A save that arrives after the deadline is refused, since
    /// accepting it would reopen the very hole the deadline is there to close.
    /// </remarks>
    public async Task<Result<bool>> SaveProgressAsync(
        string email, SaveExamProgressRequest request, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<bool>.Failure(own.Error!, own.Status);

        var attempt = await _db.ExamAttempts
            .FirstOrDefaultAsync(a => a.AttemptId == request.AttemptId, ct);

        if (attempt is null)
            return Result<bool>.Failure("That attempt no longer exists.", ResultStatus.NotFound);

        if (!string.Equals(attempt.StudentRegistrationId, own.Value!, StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Failure("That attempt belongs to someone else.", ResultStatus.Forbidden);

        if (attempt.SubmittedOn is not null)
            return Result<bool>.Failure("This paper has already been submitted.", ResultStatus.Conflict);

        if (DateTime.Now > attempt.DueOn + SubmitGrace)
            return Result<bool>.Failure("Your time on this paper has ended.", ResultStatus.Conflict);

        var valid = await _db.ExamQuestions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId)
            .Select(q => q.QuestionId)
            .ToListAsync(ct);

        attempt.Answers = FormatAnswers(
            request.Answers ?? new Dictionary<int, char>(), valid.ToHashSet());

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Records one instance of the student leaving the exam window. Counted
    /// server-side against the attempt — not just shown as a client-side warning
    /// — so a page reload cannot reset the strike count, and the auto-submit
    /// decision at the limit cannot be skipped by a browser that simply stops
    /// reporting further violations.
    /// </summary>
    public async Task<Result<ExamViolationResultDto>> RecordViolationAsync(
        string email, RecordExamViolationRequest request, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<ExamViolationResultDto>.Failure(own.Error!, own.Status);

        var attempt = await _db.ExamAttempts
            .FirstOrDefaultAsync(a => a.AttemptId == request.AttemptId, ct);

        if (attempt is null)
            return Result<ExamViolationResultDto>.Failure("That attempt no longer exists.", ResultStatus.NotFound);

        if (!string.Equals(attempt.StudentRegistrationId, own.Value!, StringComparison.OrdinalIgnoreCase))
            return Result<ExamViolationResultDto>.Failure("That attempt belongs to someone else.", ResultStatus.Forbidden);

        // Already submitted (e.g. the tab fired one more event on its way out after
        // an earlier violation auto-submitted it) — report where the count landed
        // without incrementing further; there is nothing left to protect.
        if (attempt.SubmittedOn is not null)
        {
            return Result<ExamViolationResultDto>.Success(
                new ExamViolationResultDto(attempt.ViolationCount, ViolationLimit, true));
        }

        attempt.ViolationCount++;
        var mustSubmit = attempt.ViolationCount >= ViolationLimit;
        if (mustSubmit) attempt.Flagged = true;

        _logger.LogWarning(
            "Exam violation: attempt {AttemptId} ({RegId}), reason '{Reason}', count now {Count}.",
            attempt.AttemptId, attempt.StudentRegistrationId, request.Reason, attempt.ViolationCount);

        await _db.SaveChangesAsync(ct);

        return Result<ExamViolationResultDto>.Success(
            new ExamViolationResultDto(attempt.ViolationCount, ViolationLimit, mustSubmit));
    }

    /// <summary>
    /// Marks the paper and stores the result. Answers are checked against the
    /// database, never against anything the browser sent about correctness.
    /// </summary>
    public async Task<Result<ExamResultDto>> SubmitAsync(
        string email, SubmitExamRequest request, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<ExamResultDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var attempt = await _db.ExamAttempts
            .FirstOrDefaultAsync(a => a.AttemptId == request.AttemptId, ct);

        if (attempt is null)
            return Result<ExamResultDto>.Failure("That attempt no longer exists.", ResultStatus.NotFound);

        // The attempt id comes from the client, so ownership is checked rather than
        // assumed — otherwise one student could submit against another's paper.
        if (!string.Equals(attempt.StudentRegistrationId, regId, StringComparison.OrdinalIgnoreCase))
            return Result<ExamResultDto>.Failure("That attempt belongs to someone else.", ResultStatus.Forbidden);

        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstAsync(p => p.ExamId == attempt.ExamId, ct);

        if (attempt.SubmittedOn is not null)
            return Result<ExamResultDto>.Success(BuildResult(attempt, paper));

        var now = DateTime.Now;
        var late = now > attempt.DueOn + SubmitGrace;

        var questions = await _db.ExamQuestions
            .AsNoTracking()
            .Where(q => q.ExamId == attempt.ExamId)
            .ToListAsync(ct);

        /* Past the deadline, the answers in the request are not accepted: the clock
           is the server's, and a browser that kept the page open past time up must
           not be able to keep answering. What is marked instead is the last copy
           autosaved while the paper was still open, so a student whose connection
           dropped at the last minute keeps everything they had actually chosen.

           The paper is then stamped as submitted at the deadline rather than now —
           it closed when the time ran out, whatever hour this request arrived. */
        var answers = late
            ? ParseAnswers(attempt.Answers)
            : request.Answers ?? new Dictionary<int, char>();

        var score = 0;
        var correctCount = 0;

        foreach (var question in questions)
        {
            if (!answers.TryGetValue(question.QuestionId, out var picked))
                continue;

            if (char.ToUpperInvariant(picked) != char.ToUpperInvariant(question.CorrectOption))
                continue;

            score += question.Marks;
            correctCount++;
        }

        attempt.Answers = FormatAnswers(answers, questions.Select(q => q.QuestionId).ToHashSet());
        attempt.SubmittedOn = late ? attempt.DueOn : now;
        attempt.Score = score;
        attempt.TotalMarks = questions.Sum(q => q.Marks);
        attempt.CorrectCount = correctCount;
        attempt.QuestionCount = questions.Count;
        attempt.AutoSubmitted = late;

        await _db.SaveChangesAsync(ct);

        return Result<ExamResultDto>.Success(BuildResult(attempt, paper));
    }

    public async Task<Result<IReadOnlyList<ExamResultDto>>> GetMyResultsAsync(
        string email, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<IReadOnlyList<ExamResultDto>>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var rows = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.StudentRegistrationId == regId && a.SubmittedOn != null)
            .OrderByDescending(a => a.SubmittedOn)
            .Join(_db.ExamPapers.AsNoTracking(),
                  a => a.ExamId, p => p.ExamId,
                  (a, p) => new { Attempt = a, Paper = p })
            .ToListAsync(ct);

        var results = rows.Select(r => BuildResult(r.Attempt, r.Paper)).ToList();

        return Result<IReadOnlyList<ExamResultDto>>.Success(results);
    }

    /* ============================================ admin: practical marks */

    /// <summary>
    /// The students who may be marked on a practical paper: everyone active on its
    /// course, with whatever score has already been entered.
    /// </summary>
    public async Task<Result<IReadOnlyList<PracticalRosterRowDto>>> GetPracticalRosterAsync(
        int examId, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<IReadOnlyList<PracticalRosterRowDto>>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        if (!string.Equals(paper.PaperType, "Practical", StringComparison.OrdinalIgnoreCase))
            return Result<IReadOnlyList<PracticalRosterRowDto>>.Failure(
                "Marks are only entered by hand for a practical paper. A theory paper is marked by the portal.");

        var marks = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.ExamId == examId)
            .Select(a => new { a.StudentRegistrationId, a.Score, a.SubmittedOn, a.EnteredBy })
            .ToListAsync(ct);

        var scheduledHere = await _db.ExamSchedules.AsNoTracking()
            .Where(s => s.ExamId == examId)
            .Select(s => s.StudentRegistrationId)
            .ToListAsync(ct);

        // Enrolled students, plus anyone already marked or scheduled on this paper —
        // a mark still has to be correctable after the student completes the course.
        var pinned = marks.Select(m => m.StudentRegistrationId).Concat(scheduledHere).ToList();

        var students = await StudentsForPaperAsync(paper, ct, pinned);

        var rows = students.Select(s =>
        {
            var mark = marks.FirstOrDefault(m =>
                string.Equals(m.StudentRegistrationId, s.RegistrationId, StringComparison.OrdinalIgnoreCase));

            return new PracticalRosterRowDto(
                s.RegistrationId!, s.Name, mark?.Score, paper.TotalMarks,
                mark?.SubmittedOn, mark?.EnteredBy);
        }).ToList();

        return Result<IReadOnlyList<PracticalRosterRowDto>>.Success(rows);
    }

    /// <summary>
    /// Records or corrects one student's practical mark. The mark is stored as an
    /// <see cref="ExamAttempt"/> like any other result, just one with
    /// <c>Source = "Entered"</c> and no questions behind it.
    /// </summary>
    public async Task<Result<bool>> SavePracticalMarkAsync(
        PracticalMarkRequest request, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == request.ExamId, ct);

        if (paper is null)
            return Result<bool>.Failure("That exam paper no longer exists.", ResultStatus.NotFound);

        if (!string.Equals(paper.PaperType, "Practical", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Failure(
                "Only a practical paper is marked by hand. A theory paper is marked by the portal.");

        if (request.Score > paper.TotalMarks)
            return Result<bool>.Failure(
                $"The mark cannot exceed the paper's total of {paper.TotalMarks}.");

        var regId = request.RegistrationId.Trim();

        var student = await _db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RegistrationId == regId, ct);

        if (student is null)
            return Result<bool>.Failure($"No student found with registration id '{regId}'.", ResultStatus.NotFound);

        if (student.CourseId != paper.CourseId)
            return Result<bool>.Failure(
                "That student is not enrolled on this paper's course.");

        var now = DateTime.Now;

        var attempt = await _db.ExamAttempts
            .FirstOrDefaultAsync(a => a.ExamId == request.ExamId
                                      && a.StudentRegistrationId == regId, ct);

        if (attempt is null)
        {
            attempt = new ExamAttempt
            {
                ExamId = paper.ExamId,
                StudentRegistrationId = regId,
                CourseId = paper.CourseId,
                TermNumber = paper.TermNumber,
                PaperType = paper.PaperType,
                Source = "Entered",
                StartedOn = now,
                // Nothing is timed; the deadline is only there to satisfy the shape.
                DueOn = now,
                CreatedOn = now
            };

            _db.ExamAttempts.Add(attempt);
        }

        attempt.Score = request.Score;
        attempt.TotalMarks = paper.TotalMarks;
        attempt.SubmittedOn = now;
        attempt.EnteredBy = actor;
        attempt.Source = "Entered";

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /* ================================== student: the combined standing */

    /// <summary>
    /// The student's whole examination, course by course: each term's theory and
    /// practical, and the average, percentage and grade across the four papers.
    /// </summary>
    /// <remarks>
    /// The percentage is marks obtained over marks available, not the mean of two
    /// percentages — those differ whenever the theory and practical carry different
    /// totals, and only the former is the real result. The average mark per paper is
    /// reported alongside it.
    /// </remarks>
    public async Task<Result<StudentExamResultsDto>> GetMyCourseResultsAsync(
        string email, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<StudentExamResultsDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId == regId)
            .Select(s => new { s.Name, s.CourseId, CourseName = s.Course != null ? s.Course.Name : null })
            .FirstOrDefaultAsync(ct);

        var attempts = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.StudentRegistrationId == regId)
            .Join(_db.ExamPapers.AsNoTracking(), a => a.ExamId, p => p.ExamId,
                  (a, p) => new { Attempt = a, Paper = p })
            .ToListAsync(ct);

        // Every course the student has been examined on, plus their current one, so
        // the page is not blank before the first paper is sat.
        var courseIds = attempts
            .Select(x => x.Attempt.CourseId)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .Concat(student?.CourseId is int cid ? [cid] : Array.Empty<int>())
            .Distinct()
            .ToList();

        var courseNames = await _db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Cid))
            .ToDictionaryAsync(c => c.Cid, c => c.Name, ct);

        /* ---- practical marks typed on the Marks screen ---------------------
           The institute also records practicals on the legacy Marks register,
           which is a separate store with no link to a course, term or paper.
           A mark recorded there would otherwise leave the term reading
           "Awaiting mark" forever.

           It is used ONLY when the student has exactly one such mark. With two
           or more there is nothing in the row to say which examination each
           belongs to, and guessing would put a mark against the wrong term —
           worse than showing nothing. In that case the term stays awaiting,
           and the fix is to enter the mark on the practical paper's own sheet. */

        var registerPractical = await FindSingleRegisterPracticalAsync(regId, ct);

        var courses = new List<CourseExamResultDto>();

        foreach (var courseId in courseIds)
        {
            var terms = new List<TermResultDto>();

            // Terms are not capped, so the list comes from what actually exists for
            // this course; term 1 is always shown so the page is not blank before
            // the first paper is sat.
            var termNumbers = attempts
                .Where(x => x.Attempt.CourseId == courseId && x.Attempt.TermNumber.HasValue)
                .Select(x => x.Attempt.TermNumber!.Value)
                .Append(1)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            foreach (var termNumber in termNumbers)
            {
                var theory = FindPaper(attempts, courseId, termNumber, "Theory");
                var practical = FindPaper(attempts, courseId, termNumber, "Practical");

                // Fall back to the register only where the portal has nothing, and
                // only for a term whose theory has actually been sat — otherwise an
                // untouched future term would pick the mark up.
                if (!practical.IsComplete && theory.IsComplete && registerPractical is not null)
                {
                    practical = practical with
                    {
                        Score = registerPractical.Obtained,
                        TotalMarks = registerPractical.Full,
                        CompletedOn = registerPractical.RecordedOn,
                        Title = "Entered on the Marks register",
                        IsComplete = true,
                        FromMarksRegister = true
                    };
                }

                var obtained = (theory.Score ?? 0) + (practical.Score ?? 0);
                var full = (theory.TotalMarks ?? 0) + (practical.TotalMarks ?? 0);
                var complete = theory.IsComplete && practical.IsComplete;

                var percent = full <= 0
                    ? 0m
                    : decimal.Round(obtained * 100m / full, 2, MidpointRounding.AwayFromZero);

                terms.Add(new TermResultDto(
                    termNumber, theory, practical, obtained, full, percent,
                    // A grade before both papers are in would be misleading.
                    complete ? Grade(percent) : null,
                    complete));
            }

            var papersCompleted = terms.Sum(t =>
                (t.Theory.IsComplete ? 1 : 0) + (t.Practical.IsComplete ? 1 : 0));

            var totalObtained = terms.Sum(t => t.ObtainedMarks);
            var totalFull = terms.Sum(t => t.FullMarks);

            var overallPercent = totalFull <= 0
                ? 0m
                : decimal.Round(totalObtained * 100m / totalFull, 2, MidpointRounding.AwayFromZero);

            var averageMarks = papersCompleted == 0
                ? 0m
                : decimal.Round((decimal)totalObtained / papersCompleted, 2, MidpointRounding.AwayFromZero);

            courses.Add(new CourseExamResultDto(
                courseId,
                courseNames.GetValueOrDefault(courseId),
                terms,
                papersCompleted,
                // One theory plus one practical for each term that exists.
                terms.Count * 2,
                totalObtained,
                totalFull,
                averageMarks,
                overallPercent,
                Grade(overallPercent),
                papersCompleted == terms.Count * 2));
        }

        return Result<StudentExamResultsDto>.Success(new StudentExamResultsDto(
            regId, student?.Name, courses, DateTime.Now));
    }

    /// <summary>One of the four slots, whether or not it has been sat yet.</summary>
    private static TermPaperResultDto FindPaper<T>(
        IEnumerable<T> rows, int courseId, int termNumber, string paperType)
        where T : class
    {
        dynamic? match = rows.Cast<dynamic>().FirstOrDefault(x =>
            x.Attempt.CourseId == courseId
            && x.Attempt.TermNumber == termNumber
            && x.Attempt.PaperType == paperType);

        if (match is null)
            return new TermPaperResultDto(termNumber, paperType, null, null, null, null, null, false);

        // Complete means marked: a theory paper submitted, or a practical entered.
        bool isComplete = match.Attempt.SubmittedOn is not null && match.Attempt.Score is not null;

        return new TermPaperResultDto(
            termNumber,
            paperType,
            (int?)match.Paper.ExamId,
            (string?)match.Paper.Title,
            (int?)match.Attempt.Score,
            (int?)(match.Attempt.TotalMarks ?? match.Paper.TotalMarks),
            (DateTime?)match.Attempt.SubmittedOn,
            isComplete);
    }


    /* ================================ admin: scheduling and access codes */

    public async Task<Result<IReadOnlyList<ExamScheduleRowDto>>> GetScheduleAsync(
        int examId, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is null)
            return Result<IReadOnlyList<ExamScheduleRowDto>>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        var schedules = await _db.ExamSchedules.AsNoTracking()
            .Where(s => s.ExamId == examId)
            .ToListAsync(ct);

        var attempts = await _db.ExamAttempts.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .Select(a => new { a.StudentRegistrationId, a.SubmittedOn })
            .ToListAsync(ct);

        // Anyone already involved with this paper stays on the sheet whatever their
        // status, so a sitting can always be corrected or withdrawn.
        var pinned = schedules.Select(s => s.StudentRegistrationId)
            .Concat(attempts.Select(a => a.StudentRegistrationId))
            .ToList();

        var students = await StudentsForPaperAsync(paper, ct, pinned);

        // On a Practical's own sheet, the office needs to see at a glance who has
        // actually finished Theory yet — SaveScheduleAsync enforces it, this is
        // what lets the sheet show it before anyone tries.
        Dictionary<string, DateTime>? theorySubmitted = null;

        if (!string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase))
        {
            var theoryPaper = await _db.ExamPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.CourseId == paper.CourseId
                                          && p.TermNumber == paper.TermNumber
                                          && p.PaperType == "Theory", ct);

            if (theoryPaper is not null)
            {
                theorySubmitted = await _db.ExamAttempts.AsNoTracking()
                    .Where(a => a.ExamId == theoryPaper.ExamId && a.SubmittedOn != null)
                    .ToDictionaryAsync(
                        a => a.StudentRegistrationId, a => a.SubmittedOn!.Value,
                        StringComparer.OrdinalIgnoreCase, ct);
            }
        }

        var rows = students.Select(s =>
        {
            var schedule = schedules.FirstOrDefault(x =>
                string.Equals(x.StudentRegistrationId, s.RegistrationId, StringComparison.OrdinalIgnoreCase));

            var attempt = attempts.FirstOrDefault(a =>
                string.Equals(a.StudentRegistrationId, s.RegistrationId, StringComparison.OrdinalIgnoreCase));

            DateTime theoryOn = default;
            theorySubmitted?.TryGetValue(s.RegistrationId!, out theoryOn);

            return new ExamScheduleRowDto(
                s.RegistrationId!, s.Name, s.GuardianName,
                schedule?.ScheduleId, schedule?.ScheduledOn, schedule?.AccessCode,
                schedule?.CodeIssuedOn, schedule?.CodeIssuedBy,
                attempt is not null, attempt?.SubmittedOn,
                s.IsActive == true, s.IsComplete == true,
                theorySubmitted is null ? null : (theoryOn == default ? null : theoryOn));
        }).ToList();

        return Result<IReadOnlyList<ExamScheduleRowDto>>.Success(rows);
    }

    public async Task<Result<ExamScheduleRowDto>> SaveScheduleAsync(
        ExamScheduleSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == request.ExamId, ct);

        if (paper is null)
            return Result<ExamScheduleRowDto>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        var regId = request.RegistrationId.Trim();

        var student = await _db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RegistrationId == regId, ct);

        if (student is null)
            return Result<ExamScheduleRowDto>.Failure(
                $"No student found with registration id '{regId}'.", ResultStatus.NotFound);

        if (paper.CourseId is not null && student.CourseId != paper.CourseId)
            return Result<ExamScheduleRowDto>.Failure(
                "That student is not enrolled on this paper's course.");

        // A finished student does not come back in to sit an examination. Blocked
        // here as well as hidden from the sheet, so it cannot happen by direct call
        // either. Moving an existing sitting stays allowed — see below.
        var alreadyScheduledHere = await _db.ExamSchedules
            .AnyAsync(s => s.ExamId == paper.ExamId && s.StudentRegistrationId == regId, ct);

        if (!student.IsActive == true && !alreadyScheduledHere)
        {
            return Result<ExamScheduleRowDto>.Failure(
                student.IsComplete == true
                    ? $"{student.Name} has completed this course, so there is no examination to sit."
                    : $"{student.Name} is not an active student.");
        }

        // A sitting outside the paper's own window is a sitting the student can
        // never take: the window applies to everyone and shuts first. Caught here
        // rather than left to fail silently at the desk, which is where it would
        // otherwise surface — as "Closed" on the student's screen, with a valid
        // code and a printed admit card in their hand.
        var windowFault = ScheduleOutsidePaperWindow(paper, request.ScheduledOn);

        if (windowFault is not null)
            return Result<ExamScheduleRowDto>.Failure(windowFault);

        var now = DateTime.Now;

        // Moving a sitting after the student has opened the paper would change the
        // rules under them, and the card they are holding would be wrong.
        var attempt = await _db.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ExamId == paper.ExamId
                                      && a.StudentRegistrationId == regId, ct);

        var schedule = await _db.ExamSchedules
            .FirstOrDefaultAsync(s => s.ExamId == paper.ExamId
                                      && s.StudentRegistrationId == regId, ct);

        if (attempt is not null && schedule is not null)
        {
            return Result<ExamScheduleRowDto>.Failure(
                attempt.SubmittedOn is not null
                    ? "This student has already sat the paper, so the sitting cannot be moved."
                    : "This student has the paper open right now. Wait until they submit.",
                ResultStatus.Conflict);
        }

        var isTheory = string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase);

        // The Practical date is the office's call to make once Theory is behind
        // the student, not before — sitting a practical for a paper someone has
        // not yet been examined on in theory is not a sequence this puts a date on.
        if (!isTheory)
        {
            var theoryPaper = await _db.ExamPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.CourseId == paper.CourseId
                                          && p.TermNumber == paper.TermNumber
                                          && p.PaperType == "Theory", ct);

            var theorySubmitted = theoryPaper is not null && await _db.ExamAttempts.AsNoTracking()
                .AnyAsync(a => a.ExamId == theoryPaper.ExamId
                              && a.StudentRegistrationId == regId
                              && a.SubmittedOn != null, ct);

            if (!theorySubmitted)
            {
                return Result<ExamScheduleRowDto>.Failure(
                    $"{student.Name} has not completed the Theory paper for this examination yet. " +
                    "The Practical date can only be set once Theory is submitted.");
            }
        }

        if (schedule is null)
        {
            schedule = new ExamSchedule
            {
                ExamId = paper.ExamId,
                StudentRegistrationId = regId,
                ScheduledOn = request.ScheduledOn,
                // A practical is examined in the room, so it never needs a code.
                AccessCode = isTheory ? NewAccessCode() : null,
                CodeIssuedOn = isTheory ? now : null,
                CodeIssuedBy = isTheory ? actor : null,
                CreatedBy = actor,
                CreatedOn = now
            };

            _db.ExamSchedules.Add(schedule);
        }
        else
        {
            schedule.ScheduledOn = request.ScheduledOn;
            schedule.ModifiedBy = actor;
            schedule.ModifiedOn = now;

            // Re-scheduling keeps the code already printed on the admit card;
            // regenerating it is a separate, deliberate act.
            if (isTheory && string.IsNullOrWhiteSpace(schedule.AccessCode))
            {
                schedule.AccessCode = NewAccessCode();
                schedule.CodeIssuedOn = now;
                schedule.CodeIssuedBy = actor;
            }
        }

        await _db.SaveChangesAsync(ct);

        return Result<ExamScheduleRowDto>.Success(new ExamScheduleRowDto(
            regId, student.Name, student.GuardianName,
            schedule.ScheduleId, schedule.ScheduledOn, schedule.AccessCode,
            schedule.CodeIssuedOn, schedule.CodeIssuedBy,
            attempt is not null, attempt?.SubmittedOn,
            student.IsActive == true, student.IsComplete == true));
    }

    /// <summary>
    /// Schedules a batch at one date and time. Every student still gets their own
    /// code — one code for the room would be a number the room could pass around.
    /// </summary>
    public async Task<Result<ExamScheduleBulkResultDto>> ScheduleBatchAsync(
        ExamScheduleBulkRequest request, string? actor, CancellationToken ct = default)
    {
        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == request.ExamId, ct);

        if (paper is null)
            return Result<ExamScheduleBulkResultDto>.Failure(
                "That exam paper no longer exists.", ResultStatus.NotFound);

        var windowFault = ScheduleOutsidePaperWindow(paper, request.ScheduledOn);

        if (windowFault is not null)
            return Result<ExamScheduleBulkResultDto>.Failure(windowFault);

        // No pinning here: a batch schedules the students who are still enrolled.
        // Someone who has finished the course is not swept back in by "schedule
        // everyone".
        var students = await StudentsForPaperAsync(paper, ct);

        if (request.RegistrationIds is { Count: > 0 })
        {
            var wanted = request.RegistrationIds
                .Select(r => r.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            students = students.Where(s => wanted.Contains(s.RegistrationId!)).ToList();
        }

        var existing = await _db.ExamSchedules
            .Where(s => s.ExamId == paper.ExamId)
            .ToListAsync(ct);

        var sat = await _db.ExamAttempts.AsNoTracking()
            .Where(a => a.ExamId == paper.ExamId)
            .Select(a => a.StudentRegistrationId)
            .ToListAsync(ct);

        var satSet = sat.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isTheory = string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase);
        var now = DateTime.Now;

        // Same rule as the single-student save: a Practical batch can only pick up
        // students who have actually submitted Theory for this course and term.
        HashSet<string>? theoryDone = null;

        if (!isTheory)
        {
            var theoryPaper = await _db.ExamPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.CourseId == paper.CourseId
                                          && p.TermNumber == paper.TermNumber
                                          && p.PaperType == "Theory", ct);

            theoryDone = theoryPaper is null
                ? []
                : (await _db.ExamAttempts.AsNoTracking()
                    .Where(a => a.ExamId == theoryPaper.ExamId && a.SubmittedOn != null)
                    .Select(a => a.StudentRegistrationId)
                    .ToListAsync(ct))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var scheduled = 0;
        var skippedScheduled = 0;
        var skippedSat = 0;
        var skippedTheoryNotDone = 0;

        foreach (var student in students)
        {
            var regId = student.RegistrationId!;

            // Never move a student who has already opened the paper.
            if (satSet.Contains(regId))
            {
                skippedSat++;
                continue;
            }

            if (theoryDone is not null && !theoryDone.Contains(regId))
            {
                skippedTheoryNotDone++;
                continue;
            }

            var row = existing.FirstOrDefault(s =>
                string.Equals(s.StudentRegistrationId, regId, StringComparison.OrdinalIgnoreCase));

            if (row is not null && !request.OverwriteExisting)
            {
                skippedScheduled++;
                continue;
            }

            if (row is null)
            {
                _db.ExamSchedules.Add(new ExamSchedule
                {
                    ExamId = paper.ExamId,
                    StudentRegistrationId = regId,
                    ScheduledOn = request.ScheduledOn,
                    AccessCode = isTheory ? NewAccessCode() : null,
                    CodeIssuedOn = isTheory ? now : null,
                    CodeIssuedBy = isTheory ? actor : null,
                    CreatedBy = actor,
                    CreatedOn = now
                });
            }
            else
            {
                row.ScheduledOn = request.ScheduledOn;
                row.ModifiedBy = actor;
                row.ModifiedOn = now;

                if (isTheory && string.IsNullOrWhiteSpace(row.AccessCode))
                {
                    row.AccessCode = NewAccessCode();
                    row.CodeIssuedOn = now;
                    row.CodeIssuedBy = actor;
                }
            }

            scheduled++;
        }

        await _db.SaveChangesAsync(ct);

        return Result<ExamScheduleBulkResultDto>.Success(
            new ExamScheduleBulkResultDto(scheduled, skippedScheduled, skippedSat, skippedTheoryNotDone));
    }

    public async Task<Result<ExamScheduleRowDto>> RegenerateAccessCodeAsync(
        int examId, string registrationId, string? actor, CancellationToken ct = default)
    {
        var regId = registrationId.Trim();

        var schedule = await _db.ExamSchedules
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.StudentRegistrationId == regId, ct);

        if (schedule is null)
            return Result<ExamScheduleRowDto>.Failure(
                "That student has not been scheduled for this paper.", ResultStatus.NotFound);

        var paper = await _db.ExamPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ExamId == examId, ct);

        if (paper is not null
            && !string.Equals(paper.PaperType, "Theory", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ExamScheduleRowDto>.Failure(
                "A practical is examined in the room, so it has no access code.");
        }

        var attempt = await _db.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.StudentRegistrationId == regId, ct);

        if (attempt?.SubmittedOn is not null)
            return Result<ExamScheduleRowDto>.Failure(
                "This student has already sat the paper.", ResultStatus.Conflict);

        var now = DateTime.Now;

        schedule.AccessCode = NewAccessCode();
        schedule.CodeIssuedOn = now;
        schedule.CodeIssuedBy = actor;
        schedule.ModifiedBy = actor;
        schedule.ModifiedOn = now;

        await _db.SaveChangesAsync(ct);

        var student = await _db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RegistrationId == regId, ct);

        return Result<ExamScheduleRowDto>.Success(new ExamScheduleRowDto(
            regId, student?.Name, student?.GuardianName,
            schedule.ScheduleId, schedule.ScheduledOn, schedule.AccessCode,
            schedule.CodeIssuedOn, schedule.CodeIssuedBy,
            attempt is not null, attempt?.SubmittedOn,
            student?.IsActive ?? false, student?.IsComplete ?? false));
    }

    public async Task<Result<bool>> RemoveScheduleAsync(
        int examId, string registrationId, CancellationToken ct = default)
    {
        var regId = registrationId.Trim();

        var schedule = await _db.ExamSchedules
            .FirstOrDefaultAsync(s => s.ExamId == examId && s.StudentRegistrationId == regId, ct);

        if (schedule is null)
            return Result<bool>.Failure(
                "That student has not been scheduled for this paper.", ResultStatus.NotFound);

        var attempted = await _db.ExamAttempts
            .AnyAsync(a => a.ExamId == examId && a.StudentRegistrationId == regId, ct);

        if (attempted)
            return Result<bool>.Failure(
                "This student has already opened the paper, so the sitting cannot be withdrawn.",
                ResultStatus.Conflict);

        _db.ExamSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ============================================ student: the admit card */

    public async Task<Result<IReadOnlyList<AdmitCardDto>>> GetAdmitCardsAsync(
        string email, CancellationToken ct = default)
    {
        var own = await _reports.ResolveOwnRegistrationIdAsync(email, ct);

        if (!own.Succeeded)
            return Result<IReadOnlyList<AdmitCardDto>>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId == regId)
            .Select(s => new { s.Name, s.GuardianName, s.CourseId })
            .FirstOrDefaultAsync(ct);

        // Only papers this student has actually been scheduled for. No schedule,
        // no date to print, no card.
        var scheduled = await _db.ExamSchedules.AsNoTracking()
            .Where(s => s.StudentRegistrationId == regId)
            .Join(_db.ExamPapers.AsNoTracking(),
                s => s.ExamId, p => p.ExamId,
                (s, p) => new
                {
                    s.ScheduledOn,
                    p.ExamId, p.Title, p.PaperType, p.TermNumber,
                    p.CourseId, p.DurationMinutes,
                    CourseName = p.Course != null ? p.Course.Name : null
                })
            .ToListAsync(ct);

        // One card per examination of a course — the legacy card's Theory and
        // Practical rows are the two papers of a single term.
        var cards = scheduled
            .GroupBy(x => new { x.CourseId, x.TermNumber })
            .OrderBy(g => g.Key.TermNumber)
            .Select(g =>
            {
                var theory = g.FirstOrDefault(x =>
                    string.Equals(x.PaperType, "Theory", StringComparison.OrdinalIgnoreCase));

                var practical = g.FirstOrDefault(x =>
                    string.Equals(x.PaperType, "Practical", StringComparison.OrdinalIgnoreCase));

                return new AdmitCardDto(
                    regId, student?.Name, student?.GuardianName,
                    g.Key.CourseId,
                    g.Select(x => x.CourseName).FirstOrDefault(n => n is not null),
                    g.Key.TermNumber,
                    new AdmitCardPaperDto(
                        "Theory", theory?.ExamId, theory?.Title,
                        theory?.ScheduledOn, theory?.DurationMinutes),
                    new AdmitCardPaperDto(
                        "Practical", practical?.ExamId, practical?.Title,
                        practical?.ScheduledOn, practical?.DurationMinutes),
                    DateTime.Now);
            })
            .ToList();

        return Result<IReadOnlyList<AdmitCardDto>>.Success(cards);
    }
    /* ============================================================ helpers */

    private sealed record RegisterPractical(int Obtained, int Full, DateTime? RecordedOn);

    /// <summary>
    /// The student's practical mark from the legacy Marks register, but only when
    /// there is exactly one.
    /// </summary>
    /// <remarks>
    /// A <c>dbo.Marks</c> row holds four free-text subject slots and nothing that
    /// says which examination it belongs to. With a single practical mark the
    /// attribution is unambiguous; with two or more it would be a guess, and a mark
    /// placed against the wrong examination is worse than one not shown at all — so
    /// this returns null and the term stays awaiting.
    /// </remarks>
    private async Task<RegisterPractical?> FindSingleRegisterPracticalAsync(
        string regId, CancellationToken ct)
    {
        var rows = await _db.Marks
            .AsNoTracking()
            .Where(m => m.StudentRegistrationId == regId)
            .Select(m => new
            {
                m.ExamDate,
                m.Subject1, m.FullMarks1, m.ObtainedMarks1,
                m.Subject2, m.FullMarks2, m.ObtainedMarks2,
                m.Subject3, m.FullMarks3, m.ObtainedMarks3,
                m.Subject4, m.FullMarks4, m.ObtainedMarks4
            })
            .ToListAsync(ct);

        var found = new List<RegisterPractical>();

        foreach (var row in rows)
        {
            var recordedOn = row.ExamDate?.ToDateTime(TimeOnly.MinValue);

            void Consider(string? subject, int? full, int? obtained)
            {
                if (subject is null
                    || subject.IndexOf("practical", StringComparison.OrdinalIgnoreCase) < 0
                    || full is not > 0)
                {
                    return;
                }

                found.Add(new RegisterPractical(obtained ?? 0, full.Value, recordedOn));
            }

            Consider(row.Subject1, row.FullMarks1, row.ObtainedMarks1);
            Consider(row.Subject2, row.FullMarks2, row.ObtainedMarks2);
            Consider(row.Subject3, row.FullMarks3, row.ObtainedMarks3);
            Consider(row.Subject4, row.FullMarks4, row.ObtainedMarks4);
        }

        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// The students a paper concerns: those still <b>enrolled</b> on its course. A
    /// paper with no course concerns every enrolled student.
    /// </summary>
    /// <remarks>
    /// Students who have finished the course are deliberately excluded. They do not
    /// come back in to sit an examination, so listing them only buries the people
    /// who do — on one real course the sheet ran to 43 names of whom 5 were still
    /// enrolled, and three finished students were scheduled by mistake because the
    /// roster made them look current.
    ///
    /// <para><paramref name="alsoInclude"/> pins registration ids back into the
    /// list whatever their status. Callers pass the students who already have a
    /// sitting or a mark on this paper, so someone who completes the course midway
    /// through an exam cycle does not vanish from the sheet — their row has to stay
    /// visible to be corrected or withdrawn.</para>
    /// </remarks>
    private async Task<List<StudentRosterRow>> StudentsForPaperAsync(
        ExamPaper paper, CancellationToken ct, IReadOnlyCollection<string>? alsoInclude = null)
    {
        var pinned = alsoInclude is { Count: > 0 }
            ? alsoInclude.Select(r => r.Trim()).ToList()
            : [];

        var q = _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId != null
                        && (s.IsActive == true || pinned.Contains(s.RegistrationId)));

        if (paper.CourseId is not null)
        {
            // A pinned student stays even if they have since moved course: their
            // sitting on THIS paper is what has to remain reachable.
            q = q.Where(s => s.CourseId == paper.CourseId
                             || pinned.Contains(s.RegistrationId!));
        }

        return await q
            .OrderBy(s => s.Name)
            .Select(s => new StudentRosterRow(s.RegistrationId, s.Name, s.GuardianName, s.IsActive == true, s.IsComplete == true))
            .ToListAsync(ct);
    }

    private sealed record StudentRosterRow(
        string? RegistrationId, string? Name, string? GuardianName, bool IsActive, bool IsComplete);

    /// <summary>
    /// Explains why a sitting falls outside the paper's own open window, or null
    /// when it fits.
    /// </summary>
    /// <remarks>
    /// Two windows govern a sitting: the paper's <c>OpensOn</c>/<c>ClosesOn</c>,
    /// which applies to everyone, and the student's own scheduled time. The paper's
    /// wins, so a sitting placed outside it can never be taken — the student
    /// arrives holding a valid admit card and a valid code, and the portal tells
    /// them the paper is closed. The message names both times, because the fix is
    /// to change one of them and it is not obvious which.
    /// </remarks>
    private static string? ScheduleOutsidePaperWindow(ExamPaper paper, DateTime scheduledOn)
    {
        if (paper.OpensOn is { } opens && scheduledOn < opens)
        {
            return $"That sitting is {scheduledOn:d MMM yyyy, h:mm tt}, but this paper does " +
                   $"not open until {opens:d MMM yyyy, h:mm tt}. Move the sitting later, or " +
                   "clear the paper's \"Opens on\".";
        }

        if (paper.ClosesOn is { } closes && scheduledOn > closes)
        {
            return $"That sitting is {scheduledOn:d MMM yyyy, h:mm tt}, but this paper closes " +
                   $"at {closes:d MMM yyyy, h:mm tt}, so the student could never take it. " +
                   "Move the sitting earlier, or extend or clear the paper's \"Closes on\".";
        }

        return null;
    }

    /// <summary>
    /// A six-digit code, drawn from the cryptographic generator rather than
    /// <c>Random</c> — the whole point of the code is that it cannot be guessed
    /// ahead of the sitting, and <c>Random</c> seeded from the clock can be.
    /// </summary>
    /// <remarks>
    /// The full range 000000–999999 is used, and the code is stored as text so a
    /// leading zero survives.
    /// </remarks>
    private static string NewAccessCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Compares the issued code with the one typed in without exiting early on the
    /// first differing digit. Six digits is a small enough space that a timing
    /// signal is worth denying.
    /// </summary>
    private static bool FixedTimeEquals(string? issued, string? supplied)
    {
        if (issued is null || supplied is null || issued.Length != supplied.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(issued), Encoding.UTF8.GetBytes(supplied));
    }

    /// <summary>
    /// A stable pseudo-random order key for one question within one attempt's
    /// shuffle. Deterministic in both inputs — same seed, same question, same key,
    /// every time — which is what lets a resumed sitting reproduce the exact order
    /// it already showed rather than reshuffling. Not cryptographic: it only needs
    /// to scatter well, not resist an adversary who already knows the seed.
    /// </summary>
    private static long ShuffleKey(int seed, int questionId)
    {
        unchecked
        {
            var h = (long)seed * 6364136223846793005L + questionId;
            h ^= h >> 33;
            h *= unchecked((long)0xff51afd7ed558ccdUL);
            h ^= h >> 33;
            return h;
        }
    }

    private static ExamResultDto BuildResult(ExamAttempt attempt, ExamPaper paper)
    {
        var score = attempt.Score ?? 0;
        var total = attempt.TotalMarks ?? paper.TotalMarks;

        var percent = total <= 0
            ? 0m
            : decimal.Round(score * 100m / total, 2, MidpointRounding.AwayFromZero);

        return new ExamResultDto(
            attempt.AttemptId,
            attempt.ExamId,
            paper.Title,
            paper.Subject,
            score,
            total,
            percent,
            attempt.CorrectCount ?? 0,
            attempt.QuestionCount ?? 0,
            paper.PassMarks,
            paper.PassMarks is null ? null : score >= paper.PassMarks,
            Grade(percent),
            attempt.SubmittedOn ?? attempt.StartedOn,
            attempt.AutoSubmitted,
            attempt.ViolationCount,
            attempt.Flagged);
    }

    /// <summary>
    /// The same bands the certificate uses, so a student sees one grading scheme
    /// across the whole system.
    /// </summary>
    private static string Grade(decimal percent) => percent switch
    {
        >= 95m => "AA",
        >= 90m => "A+",
        >= 80m => "A",
        >= 75m => "B+",
        >= 60m => "B",
        >= 50m => "C",
        _ => "D"
    };

    /// <summary>An attempt open and still inside its own deadline.</summary>
    private async Task<bool> HasLiveAttemptAsync(int examId, CancellationToken ct)
    {
        var now = DateTime.Now;

        return await _db.ExamAttempts
            .AnyAsync(a => a.ExamId == examId && a.SubmittedOn == null && a.DueOn > now, ct);
    }

    private static string FormatAnswers(IReadOnlyDictionary<int, char> answers, HashSet<int> valid)
    {
        var builder = new StringBuilder();

        foreach (var (questionId, option) in answers.OrderBy(a => a.Key))
        {
            // Anything not on this paper is ignored rather than stored.
            if (!valid.Contains(questionId)) continue;

            if (builder.Length > 0) builder.Append(',');
            builder.Append(questionId).Append(':').Append(char.ToUpperInvariant(option));
        }

        return builder.ToString();
    }

    private static Dictionary<int, char> ParseAnswers(string? stored)
    {
        var result = new Dictionary<int, char>();

        if (string.IsNullOrWhiteSpace(stored))
            return result;

        foreach (var pair in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(':', 2);

            if (parts.Length == 2
                && int.TryParse(parts[0], out var questionId)
                && parts[1].Length == 1)
            {
                result[questionId] = char.ToUpperInvariant(parts[1][0]);
            }
        }

        return result;
    }
}
