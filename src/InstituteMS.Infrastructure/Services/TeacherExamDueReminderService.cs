using InstituteMS.Application.Chat;
using InstituteMS.Application.TeacherExams;
using InstituteMS.Domain.Common;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Once a day, tells Admin and the teacher themselves about every assignment
/// that has just become due for a new sitting — three months after a pass, or
/// immediately after a fail (see <see cref="TeacherExamService.GetAssignmentsAsync"/>).
/// A reminder is sent once per due cycle, not once per day it stays unscheduled:
/// see the "already reminded" check below.
/// </summary>
public class TeacherExamDueReminderService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TeacherExamDueReminderService> _logger;

    public TeacherExamDueReminderService(
        IServiceScopeFactory scopeFactory, ILogger<TeacherExamDueReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A short initial delay so this runs after the app (and its DB
        // connection) is actually up, not racing app startup.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Teacher exam due-reminder sweep failed");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var exams = scope.ServiceProvider.GetRequiredService<ITeacherExamService>();

        var due = (await exams.GetAssignmentsAsync(ct)).Where(a => a.IsActive && a.NeedsScheduling).ToList();
        if (due.Count == 0) return;

        // A reminder already exists for this exact due cycle when a chat
        // message mentioning this course was sent to this teacher after the
        // assignment's own due date — cheap enough at this scale to check by
        // message text rather than adding another table just to dedupe.
        var adminRecipients = (await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Roles != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct))
            .Where(u => UserRoles.Split(u.Roles).Any(r => r is UserRoles.Admin or UserRoles.SuperAdmin))
            .Select(u => u.Email!)
            .ToList();

        // The system itself is the sender — a real Admin login is needed for
        // the chat conversation to exist, so the earliest active Admin stands
        // in as the reminder's "from".
        var systemActor = adminRecipients.FirstOrDefault();
        if (systemActor is null) return;

        foreach (var assignment in due)
        {
            var teacherLoginId = await db.Teachers.AsNoTracking()
                .Where(t => t.Id == assignment.TeacherId)
                .Select(t => t.LoginId)
                .FirstOrDefaultAsync(ct);

            var text = $"{assignment.TeacherName} is due for their {assignment.CourseName} competency " +
                       "exam — please schedule the next sitting.";

            var alreadyReminded = await db.ChatMessages.AsNoTracking()
                .Where(m => m.SenderEmail == systemActor && m.Body == text && m.SentOn >= assignment.DueOn)
                .AnyAsync(ct);

            if (alreadyReminded) continue;

            var recipients = adminRecipients.ToList();
            if (!string.IsNullOrWhiteSpace(teacherLoginId)) recipients.Add(teacherLoginId);

            foreach (var loginId in recipients.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(loginId, systemActor, StringComparison.OrdinalIgnoreCase)) continue;

                var conversation = await chat.StartOrGetDirectConversationAsync(systemActor, loginId, ct);
                if (!conversation.Succeeded) continue;

                await chat.SendMessageAsync(systemActor, conversation.Value, new SendMessageRequest { Body = text }, ct);
            }

            _logger.LogInformation(
                "Sent exam-due reminder for {Teacher} / {Course}", assignment.TeacherName, assignment.CourseName);
        }
    }
}
