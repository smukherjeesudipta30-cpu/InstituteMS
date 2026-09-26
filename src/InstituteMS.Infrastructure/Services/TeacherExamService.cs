using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Application.TeacherExams;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The teacher competency exam portal: Admin assigns a teacher to a course,
/// sets a sitting's date/time, AI generates that sitting's questions, and the
/// teacher takes it. A pass is due again in three months; a fail is due again
/// immediately — both surfaced through <see cref="GetAssignmentsAsync"/>
/// rather than a stored "next due date", so it can never drift from what
/// actually happened.
/// </summary>
public class TeacherExamService : ITeacherExamService
{
    private const int ViolationLimit = 3;

    private readonly AppDbContext _db;
    private readonly IExamQuestionGenerator _generator;
    private readonly IChatService _chat;
    private readonly ILogger<TeacherExamService> _logger;

    public TeacherExamService(
        AppDbContext db, IExamQuestionGenerator generator, IChatService chat, ILogger<TeacherExamService> logger)
    {
        _db = db;
        _generator = generator;
        _chat = chat;
        _logger = logger;
    }

    /* ---------------------------------------------------------- assignments */

    public async Task<IReadOnlyList<TeacherExamAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default)
    {
        var assignments = await _db.TeacherExamAssignments
            .AsNoTracking()
            .Select(a => new
            {
                a.Id, a.TeacherId, a.CourseId, a.IsActive,
                TeacherName = a.Teacher != null ? a.Teacher.Name : null,
                CourseName = a.Course != null ? a.Course.Name : null
            })
            .ToListAsync(ct);

        if (assignments.Count == 0) return [];

        var assignmentIds = assignments.Select(a => a.Id).ToList();

        // Every schedule for these assignments, each with its attempt if one
        // exists — small enough per assignment (one sitting every few months)
        // to just pull and reason about in memory.
        var schedules = await _db.TeacherExamSchedules
            .AsNoTracking()
            .Where(s => assignmentIds.Contains(s.TeacherExamAssignmentId))
            .Select(s => new
            {
                s.Id, s.TeacherExamAssignmentId, s.ScheduledOn,
                Attempt = s.Attempt == null ? null : new { s.Attempt.SubmittedOn, s.Attempt.ScorePercent, s.Attempt.Passed }
            })
            .ToListAsync(ct);

        var byAssignment = schedules.GroupBy(s => s.TeacherExamAssignmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.Now;

        return assignments.Select(a =>
        {
            var own = byAssignment.GetValueOrDefault(a.Id) ?? [];

            var pending = own
                .Where(s => s.Attempt is null || s.Attempt.SubmittedOn is null)
                .OrderBy(s => s.ScheduledOn)
                .FirstOrDefault();

            var lastCompleted = own
                .Where(s => s.Attempt is not null && s.Attempt.SubmittedOn is not null)
                .OrderByDescending(s => s.Attempt!.SubmittedOn)
                .FirstOrDefault();

            var dueOn = lastCompleted is null
                ? now
                : lastCompleted.Attempt!.Passed == true
                    ? lastCompleted.Attempt.SubmittedOn!.Value.AddMonths(3)
                    : now;

            var needsScheduling = pending is null && dueOn <= now;

            return new TeacherExamAssignmentDto(
                a.Id, a.TeacherId, a.TeacherName ?? "(unknown)", a.CourseId, a.CourseName ?? "(unknown)", a.IsActive,
                lastCompleted?.Attempt?.SubmittedOn, lastCompleted?.Attempt?.ScorePercent, lastCompleted?.Attempt?.Passed,
                pending?.ScheduledOn, needsScheduling, dueOn);
        })
        .OrderByDescending(a => a.NeedsScheduling)
        .ThenBy(a => a.DueOn)
        .ToList();
    }

    public async Task<Result<int>> CreateAssignmentAsync(
        TeacherExamAssignmentSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var existing = await _db.TeacherExamAssignments.FirstOrDefaultAsync(
            a => a.TeacherId == request.TeacherId && a.CourseId == request.CourseId, ct);

        if (existing is not null)
        {
            if (existing.IsActive)
                return Result<int>.Failure("This teacher is already assigned to this course.", ResultStatus.Conflict);

            // Re-assigning a course that was previously deactivated just turns
            // it back on, rather than creating a duplicate row.
            existing.IsActive = true;
            existing.ModifiedBy = actor;
            existing.ModifiedOn = DateTime.Now;
            await _db.SaveChangesAsync(ct);
            return Result<int>.Success(existing.Id);
        }

        var assignment = new TeacherExamAssignment
        {
            TeacherId = request.TeacherId,
            CourseId = request.CourseId,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.TeacherExamAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(assignment.Id);
    }

    public async Task<Result<bool>> SetAssignmentActiveAsync(
        int assignmentId, bool isActive, string? actor, CancellationToken ct = default)
    {
        var assignment = await _db.TeacherExamAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null)
            return Result<bool>.Failure("That assignment no longer exists.", ResultStatus.NotFound);

        assignment.IsActive = isActive;
        assignment.ModifiedBy = actor;
        assignment.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /* ------------------------------------------------------------ schedules */

    public async Task<Result<int>> ScheduleAsync(
        TeacherExamScheduleRequest request, string? actor, CancellationToken ct = default)
    {
        var assignment = await _db.TeacherExamAssignments
            .AsNoTracking()
            .Where(a => a.Id == request.TeacherExamAssignmentId)
            .Select(a => new
            {
                a.TeacherId, a.CourseId,
                TeacherName = a.Teacher != null ? a.Teacher.Name : null,
                TeacherLoginId = a.Teacher != null ? a.Teacher.LoginId : null,
                CourseName = a.Course != null ? a.Course.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (assignment is null)
            return Result<int>.Failure("That assignment no longer exists.", ResultStatus.NotFound);

        if (string.IsNullOrWhiteSpace(assignment.CourseName))
            return Result<int>.Failure("That course no longer exists.", ResultStatus.NotFound);

        var generated = await _generator.GenerateAsync(assignment.CourseName, request.QuestionCount, ct);
        if (!generated.Succeeded)
            return Result<int>.Failure(generated.Error ?? "Could not generate questions.", generated.Status);

        // The sitting is scheduled for an exact number of questions; a short
        // set (a reply cut off, or questions dropped as unusable) must not
        // quietly become a shorter exam. Extras beyond the count are dropped.
        if (generated.Value!.Count < request.QuestionCount)
            return Result<int>.Failure(
                $"The AI returned only {generated.Value.Count} usable questions of the {request.QuestionCount} " +
                "asked for. Nothing was scheduled — please try again.",
                ResultStatus.BadRequest);

        var questionSet = generated.Value.Take(request.QuestionCount).ToList();

        // Best-effort: a study guide is a bonus, not the point of scheduling,
        // so a failure here (or no key at all) never blocks the sitting itself.
        var studyGuide = await _generator.GenerateStudyGuideAsync(assignment.CourseName, ct);

        var schedule = new TeacherExamSchedule
        {
            TeacherExamAssignmentId = request.TeacherExamAssignmentId,
            ScheduledOn = request.ScheduledOn,
            StudyGuide = studyGuide.Succeeded ? studyGuide.Value : null,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.TeacherExamSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        var order = 1;
        foreach (var q in questionSet)
        {
            _db.TeacherExamQuestions.Add(new TeacherExamQuestion
            {
                TeacherExamScheduleId = schedule.Id,
                OrderNo = order++,
                QuestionText = q.QuestionText,
                OptionA = q.OptionA,
                OptionB = q.OptionB,
                OptionC = q.OptionC,
                OptionD = q.OptionD,
                CorrectOption = q.CorrectOption,
                CreatedOn = DateTime.Now
            });
        }

        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(assignment.TeacherLoginId) && !string.IsNullOrWhiteSpace(actor))
        {
            var text = $"Your {assignment.CourseName} competency exam has been scheduled for " +
                       $"{request.ScheduledOn:d MMM yyyy, h:mm tt}. You need 50% to pass.";
            await NotifyAsync([assignment.TeacherLoginId], actor, text, ct);
        }

        return Result<int>.Success(schedule.Id);
    }

    public async Task<IReadOnlyList<TeacherExamScheduleDto>> GetSchedulesAsync(
        int? teacherId, CancellationToken ct = default)
    {
        var q = _db.TeacherExamSchedules.AsNoTracking().AsQueryable();

        if (teacherId.HasValue)
            q = q.Where(s => s.Assignment!.TeacherId == teacherId.Value);

        var rows = await q
            .OrderByDescending(s => s.ScheduledOn)
            .Select(s => new
            {
                s.Id, s.TeacherExamAssignmentId, s.ScheduledOn, s.StudyGuide,
                TeacherId = s.Assignment!.TeacherId,
                TeacherName = s.Assignment.Teacher != null ? s.Assignment.Teacher.Name : null,
                CourseId = s.Assignment.CourseId,
                CourseName = s.Assignment.Course != null ? s.Assignment.Course.Name : null,
                QuestionCount = s.Questions.Count,
                s.Attempt
            })
            .ToListAsync(ct);

        var now = DateTime.Now;

        return rows.Select(r =>
        {
            var status =
                r.Attempt?.SubmittedOn is not null ? "Completed" :
                r.ScheduledOn <= now ? "Ready" : "Upcoming";

            return new TeacherExamScheduleDto(
                r.Id, r.TeacherExamAssignmentId, r.TeacherId, r.TeacherName ?? "(unknown)",
                r.CourseId, r.CourseName ?? "(unknown)", r.ScheduledOn, r.QuestionCount,
                status, r.Attempt?.ScorePercent, r.Attempt?.Passed, r.Attempt?.SubmittedOn, r.StudyGuide);
        }).ToList();
    }

    /* -------------------------------------------------------------- sitting */

    public async Task<Result<TeacherExamPaperDto>> StartAttemptAsync(
        int scheduleId, int teacherId, CancellationToken ct = default)
    {
        var schedule = await _db.TeacherExamSchedules
            .Include(s => s.Questions)
            .Include(s => s.Assignment)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

        if (schedule is null)
            return Result<TeacherExamPaperDto>.Failure("That sitting no longer exists.", ResultStatus.NotFound);

        if (schedule.Assignment?.TeacherId != teacherId)
            return Result<TeacherExamPaperDto>.Failure("That sitting is not yours.", ResultStatus.Forbidden);

        if (schedule.ScheduledOn > DateTime.Now)
            return Result<TeacherExamPaperDto>.Failure(
                $"This sitting opens at {schedule.ScheduledOn:d MMM yyyy, h:mm tt}.", ResultStatus.BadRequest);

        var attempt = await _db.TeacherExamAttempts.FirstOrDefaultAsync(a => a.TeacherExamScheduleId == scheduleId, ct);

        if (attempt is not null && attempt.SubmittedOn is not null)
            return Result<TeacherExamPaperDto>.Failure("You have already submitted this sitting.", ResultStatus.Conflict);

        if (attempt is null)
        {
            attempt = new TeacherExamAttempt
            {
                TeacherExamScheduleId = scheduleId,
                StartedOn = DateTime.Now
            };
            _db.TeacherExamAttempts.Add(attempt);
            await _db.SaveChangesAsync(ct);
        }

        var course = await _db.Courses.AsNoTracking()
            .Where(c => c.Cid == schedule.Assignment.CourseId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        var questions = schedule.Questions
            .OrderBy(q => q.OrderNo)
            .Select(q => new TeacherExamQuestionDto(q.Id, q.OrderNo, q.QuestionText, q.OptionA, q.OptionB, q.OptionC, q.OptionD))
            .ToList();

        return Result<TeacherExamPaperDto>.Success(new TeacherExamPaperDto(
            schedule.Id, course ?? "(unknown)", schedule.ScheduledOn, attempt.Id, attempt.StartedOn, questions));
    }

    public async Task<Result<TeacherExamResultDto>> SubmitAttemptAsync(
        TeacherExamSubmitRequest request, int teacherId, string? actor, CancellationToken ct = default)
    {
        var attempt = await _db.TeacherExamAttempts
            .Include(a => a.Schedule!).ThenInclude(s => s.Questions)
            .Include(a => a.Schedule!).ThenInclude(s => s.Assignment)
            .FirstOrDefaultAsync(a => a.Id == request.AttemptId, ct);

        if (attempt is null)
            return Result<TeacherExamResultDto>.Failure("That attempt no longer exists.", ResultStatus.NotFound);

        if (attempt.Schedule?.Assignment?.TeacherId != teacherId)
            return Result<TeacherExamResultDto>.Failure("That attempt is not yours.", ResultStatus.Forbidden);

        if (attempt.SubmittedOn is not null)
            return Result<TeacherExamResultDto>.Failure("This sitting has already been submitted.", ResultStatus.Conflict);

        return Result<TeacherExamResultDto>.Success(
            await ScoreAndSaveAsync(attempt, request.Answers, teacherId, actor, ct));
    }

    /// <summary>
    /// The scoring core, shared by a normal submit and an auto-submit forced
    /// by hitting the violation limit — same rules either way, since a paper
    /// closed by too many strikes is scored exactly like one closed on purpose.
    /// </summary>
    private async Task<TeacherExamResultDto> ScoreAndSaveAsync(
        TeacherExamAttempt attempt, IReadOnlyList<TeacherExamAnswerRequest> answers,
        int teacherId, string? actor, CancellationToken ct)
    {
        var questions = attempt.Schedule!.Questions.ToDictionary(q => q.Id);
        var answersByQuestion = answers.ToDictionary(a => a.QuestionId, a => a.SelectedOption);

        var correctCount = 0;
        foreach (var question in questions.Values)
        {
            answersByQuestion.TryGetValue(question.Id, out var selected);
            var isCorrect = selected is not null
                && string.Equals(selected, question.CorrectOption, StringComparison.OrdinalIgnoreCase);

            if (isCorrect) correctCount++;

            _db.TeacherExamAnswers.Add(new TeacherExamAnswer
            {
                TeacherExamAttemptId = attempt.Id,
                TeacherExamQuestionId = question.Id,
                SelectedOption = selected,
                IsCorrect = isCorrect
            });
        }

        var total = questions.Count;
        var scorePercent = total == 0 ? 0m : Math.Round(100m * correctCount / total, 2);
        var passed = scorePercent >= 50m;

        attempt.SubmittedOn = DateTime.Now;
        attempt.ScorePercent = scorePercent;
        attempt.Passed = passed;

        await _db.SaveChangesAsync(ct);

        var courseName = await _db.Courses.AsNoTracking()
            .Where(c => c.Cid == attempt.Schedule.Assignment!.CourseId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? "(unknown)";

        var teacher = await _db.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new { t.Name, t.LoginId })
            .FirstOrDefaultAsync(ct);

        await NotifyResultAsync(teacher?.Name ?? "A teacher", teacher?.LoginId, courseName, scorePercent, passed, actor, ct);

        return new TeacherExamResultDto(
            attempt.Id, attempt.TeacherExamScheduleId, courseName, total, correctCount, scorePercent, passed);
    }

    public async Task<Result<TeacherExamViolationResultDto>> RecordViolationAsync(
        RecordTeacherExamViolationRequest request, int teacherId, string? actor, CancellationToken ct = default)
    {
        var attempt = await _db.TeacherExamAttempts
            .Include(a => a.Schedule!).ThenInclude(s => s.Questions)
            .Include(a => a.Schedule!).ThenInclude(s => s.Assignment)
            .FirstOrDefaultAsync(a => a.Id == request.AttemptId, ct);

        if (attempt is null)
            return Result<TeacherExamViolationResultDto>.Failure("That attempt no longer exists.", ResultStatus.NotFound);

        if (attempt.Schedule?.Assignment?.TeacherId != teacherId)
            return Result<TeacherExamViolationResultDto>.Failure("That attempt is not yours.", ResultStatus.Forbidden);

        // Already submitted (e.g. one more event fired on its way out after an
        // earlier violation auto-submitted it) — report where the count landed
        // without incrementing further; there is nothing left to protect.
        if (attempt.SubmittedOn is not null)
        {
            return Result<TeacherExamViolationResultDto>.Success(
                new TeacherExamViolationResultDto(attempt.ViolationCount, ViolationLimit, true, null));
        }

        attempt.ViolationCount++;
        var mustSubmit = attempt.ViolationCount >= ViolationLimit;

        _logger.LogWarning(
            "Teacher exam violation: attempt {AttemptId} (teacher {TeacherId}), reason '{Reason}', count now {Count}.",
            attempt.Id, teacherId, request.Reason, attempt.ViolationCount);

        await _db.SaveChangesAsync(ct);

        TeacherExamResultDto? result = null;
        if (mustSubmit)
            result = await ScoreAndSaveAsync(attempt, request.Answers, teacherId, actor, ct);

        return Result<TeacherExamViolationResultDto>.Success(
            new TeacherExamViolationResultDto(attempt.ViolationCount, ViolationLimit, mustSubmit, result));
    }

    /* ------------------------------------------------------------- helpers */

    public async Task<Result<int>> ResolveOwnTeacherIdAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<int>.Failure("No signed-in identity to resolve.", ResultStatus.Unauthorized);

        var teacherId = await _db.Teachers.AsNoTracking()
            .Where(t => t.LoginId == email.Trim())
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        return teacherId is null
            ? Result<int>.Failure("No teacher record matches this sign-in.", ResultStatus.NotFound)
            : Result<int>.Success(teacherId.Value);
    }

    private async Task NotifyResultAsync(
        string teacherName, string? teacherLoginId, string courseName,
        decimal scorePercent, bool passed, string? actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) return;

        var verdict = passed ? "passed" : "did not pass";
        var text = $"{teacherName} {verdict} the {courseName} competency exam with {scorePercent}% " +
                   "(50% required).";

        var adminLoginIds = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Roles != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct);

        var recipients = adminLoginIds
            .Where(u => UserRoles.Split(u.Roles).Any(r => r is UserRoles.Admin or UserRoles.SuperAdmin))
            .Select(u => u.Email!)
            .ToList();

        if (!string.IsNullOrWhiteSpace(teacherLoginId)) recipients.Add(teacherLoginId);

        await NotifyAsync(recipients, actor, text, ct);
    }

    /// <summary>
    /// Best-effort, same reasoning as every other chat notice in this codebase:
    /// a failed send must never undo the exam result or schedule that already
    /// saved.
    /// </summary>
    private async Task NotifyAsync(IEnumerable<string> loginIds, string actor, string text, CancellationToken ct)
    {
        foreach (var loginId in loginIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(loginId, actor, StringComparison.OrdinalIgnoreCase)) continue;

            var conversation = await _chat.StartOrGetDirectConversationAsync(actor, loginId, ct);
            if (!conversation.Succeeded) continue;

            await _chat.SendMessageAsync(actor, conversation.Value, new SendMessageRequest { Body = text }, ct);
        }
    }
}
