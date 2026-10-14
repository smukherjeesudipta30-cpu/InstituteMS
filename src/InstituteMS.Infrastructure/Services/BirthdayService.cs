using InstituteMS.Application.Birthdays;
using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Birthdays of active students (from the student record) and of staff — Teacher,
/// Receptionist, Technical Staff, Editor, Admin and SuperAdmin — from the Users table, where a
/// date of birth exists. Anyone without one is simply left out.
/// </summary>
public class BirthdayService : IBirthdayService
{
    public const string Proprietor = "Proprietor";
    private const string ProprietorKey = "P-1";

    private static readonly string[] StaffRoles =
        [UserRoles.Teacher, UserRoles.Receptionist, UserRoles.Editor, UserRoles.Admin, UserRoles.SuperAdmin];

    private readonly AppDbContext _db;
    private readonly IChatService _chat;

    public BirthdayService(AppDbContext db, IChatService chat)
    {
        _db = db;
        _chat = chat;
    }

    public async Task<IReadOnlyList<BirthdayPersonDto>> GetUpcomingAsync(int days, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var until = today.AddDays(days);
        var results = new List<BirthdayPersonDto>();

        void Add(string key, string kind, string? name, string? phone, string? reg, string? login, DateOnly dob)
        {
            var next = Anniversary(dob, today.Year);
            if (next < today) next = Anniversary(dob, today.Year + 1);
            if (next > until) return;
            results.Add(new BirthdayPersonDto(key, kind, name?.Trim(), phone?.Trim(), reg, login, dob,
                next, next.DayNumber - today.DayNumber, next.Year - dob.Year));
        }

        // Students come from the student record; every other role from the Users
        // table, where the date of birth is required for non-student roles.
        var students = await _db.Students.AsNoTracking()
            .Where(s => s.DateOfBirth != null && s.IsActive == true)
            .Select(s => new { s.RegistrationId, s.Name, s.Phone, s.AvijanLoginEmail, s.DateOfBirth })
            .ToListAsync(ct);
        foreach (var s in students)
            Add($"S-{s.RegistrationId}", "Student", s.Name, s.Phone, s.RegistrationId, s.AvijanLoginEmail?.Trim(), s.DateOfBirth!.Value);

        var users = (await _db.Users.AsNoTracking()
            .Where(u => u.DateOfBirth != null && u.IsActive && u.Roles != null)
            .Select(u => new { u.Id, u.Name, u.Email, u.LoginId, u.PhoneNumber, u.Roles, u.DateOfBirth })
            .ToListAsync(ct))
            .Select(u => new { u.Id, u.Name, u.Email, u.LoginId, u.PhoneNumber, u.DateOfBirth, RoleList = UserRoles.Split(u.Roles) })
            .Where(u => u.RoleList.Any(r => StaffRoles.Contains(r)))
            .ToList();

        // Technical Staff are receptionists or teachers flagged IsTechnical; the
        // staff record also supplies the phone when the login account has none.
        var receptionists = await _db.Receptionists.AsNoTracking()
            .Where(r => r.LoginId != null).Select(r => new { r.LoginId, r.Phone, r.IsTechnical }).ToListAsync(ct);
        var teachers = await _db.Teachers.AsNoTracking()
            .Where(t => t.LoginId != null).Select(t => new { t.LoginId, t.Phone, t.IsTechnical }).ToListAsync(ct);
        var staff = receptionists.Concat(teachers)
            .GroupBy(x => x.LoginId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (Phone: g.Select(x => x.Phone).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)), Technical: g.Any(x => x.IsTechnical)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var u in users)
        {
            var login = (u.LoginId ?? u.Email)?.Trim();
            (string? Phone, bool Technical) match = (null, false);
            if (login is not null && staff.TryGetValue(login, out var byLogin)) match = byLogin;
            else if (u.Email is not null && staff.TryGetValue(u.Email.Trim(), out var byEmail)) match = byEmail;

            var roles = u.RoleList;
            var kind = match.Technical ? "Technical Staff"
                     : roles.Contains(UserRoles.SuperAdmin) ? "SuperAdmin"
                     : roles.Contains(UserRoles.Admin) ? "Admin"
                     : roles.Contains(UserRoles.Editor) ? "Editor"
                     : roles.Contains(UserRoles.Teacher) ? "Teacher"
                     : "Receptionist";
            Add($"U-{u.Id}", kind, u.Name, string.IsNullOrWhiteSpace(u.PhoneNumber) ? match.Phone : u.PhoneNumber,
                null, login, u.DateOfBirth!.Value);
        }

        // The Proprietor has no login account; their details are kept on their own.
        var proprietor = await _db.ProprietorProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (proprietor?.DateOfBirth is DateOnly pdob)
            Add(ProprietorKey, Proprietor, proprietor.Name, proprietor.Phone, null, null, pdob);

        return Ordered(results);
    }

    public async Task<ProprietorProfileDto> GetProprietorAsync(CancellationToken ct = default)
    {
        var p = await _db.ProprietorProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        return new ProprietorProfileDto(p?.Name ?? string.Empty, p?.DateOfBirth, p?.Phone);
    }

    public async Task<Result<ProprietorProfileDto>> SaveProprietorAsync(
        ProprietorProfileDto request, string? actor, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100) return Result<ProprietorProfileDto>.Failure("Enter the Proprietor's name.");
        if (request.DateOfBirth is null) return Result<ProprietorProfileDto>.Failure("Enter the date of birth.");
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.Today))
            return Result<ProprietorProfileDto>.Failure("Date of birth cannot be in the future.");
        var phone = request.Phone?.Trim();
        if (!string.IsNullOrEmpty(phone) && phone.Length > 20) return Result<ProprietorProfileDto>.Failure("The phone number is too long.");

        var row = await _db.ProprietorProfiles.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null) { row = new ProprietorProfile { Id = 1 }; _db.ProprietorProfiles.Add(row); }
        row.Name = name;
        row.DateOfBirth = request.DateOfBirth;
        row.Phone = string.IsNullOrEmpty(phone) ? null : phone;
        row.ModifiedBy = actor;
        row.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Result<ProprietorProfileDto>.Success(await GetProprietorAsync(ct));
    }

    /// <summary>
    /// When the chat reminders go out: three times on the day before a birthday and
    /// three times on the day itself. Each sweep sends only for the latest time
    /// already passed, so an API restart mid-afternoon sends once, not a burst.
    /// </summary>
    private static readonly TimeSpan[] ReminderTimes =
        [new(9, 0, 0), new(13, 0, 0), new(17, 0, 0)];

    private const string TodayHeading = "🎂 Birthdays today";
    private const string TomorrowHeading = "🎈 Birthdays tomorrow";

    public async Task<int> RaiseTodayNotificationsAsync(CancellationToken ct = default)
    {
        var soon = await GetUpcomingAsync(1, ct);
        var todays = soon.Where(p => p.DaysAway == 0).ToList();
        var tomorrows = soon.Where(p => p.DaysAway == 1).ToList();
        if (todays.Count == 0 && tomorrows.Count == 0) return 0;

        // Dashboard: one alert the day before and one on the day. The person's key
        // is kept in DetailValue ("…@eve" for the day before) so neither repeats.
        var dayStart = DateTime.Today;
        var already = (await _db.AdminNotifications.AsNoTracking()
            .Where(n => n.Type == AdminNotificationTypes.Birthday && n.CreatedOn >= dayStart)
            .Select(n => n.DetailValue)
            .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (p, eve) in todays.Select(p => (p, false)).Concat(tomorrows.Select(p => (p, true))))
        {
            var key = eve ? p.Key + "@eve" : p.Key;
            if (already.Contains(key)) continue;
            _db.AdminNotifications.Add(new AdminNotification
            {
                Type = AdminNotificationTypes.Birthday,
                Title = eve
                    ? $"Tomorrow is {p.Name}'s birthday ({p.Kind}, turning {p.TurningAge})"
                    : $"Today is {p.Name}'s birthday ({p.Kind}, turning {p.TurningAge}) — send a wish",
                StudentName = p.Name,
                CourseName = p.Kind,
                RegistrationId = p.RegistrationId,
                LoginId = p.LoginId,
                DetailValue = key,
                CreatedOn = DateTime.Now
            });
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync(ct);

        // Chat: at each reminder time, once.
        var now = DateTime.Now;
        var passed = ReminderTimes.Where(t => now.TimeOfDay >= t).ToList();
        if (passed.Count > 0)
        {
            var slotStart = dayStart + passed[^1];
            if (todays.Count > 0) await SendChatAsync(todays, TodayHeading, slotStart, ct);
            if (tomorrows.Count > 0) await SendChatAsync(tomorrows, TomorrowHeading, slotStart, ct);
        }
        return added;
    }

    /// <summary>
    /// The news in the chat window of every active Admin and SuperAdmin; the
    /// Proprietor's birthday goes to Editors as well, and Teacher and Student birthdays
    /// to Receptionists. A chat needs a sender, so a
    /// SuperAdmin (or, failing that, the first Admin) sends it to all the others.
    /// Skipped when this heading was already sent since <paramref name="slotStart"/>.
    /// </summary>
    private async Task SendChatAsync(
        IReadOnlyList<BirthdayPersonDto> people, string heading, DateTime slotStart, CancellationToken ct)
    {
        var office = (await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Roles != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct))
            .Select(u => new { Email = u.Email!, Roles = UserRoles.Split(u.Roles) })
            .Where(u => u.Roles.Any(r => r is UserRoles.Admin or UserRoles.SuperAdmin or UserRoles.Editor or UserRoles.Receptionist))
            .ToList();
        var senderAccount = office.FirstOrDefault(u => u.Roles.Contains(UserRoles.SuperAdmin))
                            ?? office.FirstOrDefault(u => u.Roles.Contains(UserRoles.Admin));
        if (senderAccount is null) return;
        var sender = senderAccount.Email;

        var sentThisSlot = await _db.ChatMessages.AsNoTracking()
            .AnyAsync(m => m.SenderEmail == sender && m.SentOn >= slotStart && m.Body.StartsWith(heading), ct);
        if (sentThisSlot) return;

        foreach (var person in office.Where(u => !string.Equals(u.Email, sender, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(u => u.Email, StringComparer.OrdinalIgnoreCase))
        {
            var isAdmin = person.Roles.Any(r => r is UserRoles.Admin or UserRoles.SuperAdmin);
            var isEditor = person.Roles.Contains(UserRoles.Editor);
            var theirs = isAdmin ? people
                : people.Where(p => (isEditor && p.Kind == Proprietor)
                                 || (person.Roles.Contains(UserRoles.Receptionist) && p.Kind is "Teacher" or "Student"))
                        .ToList();
            if (theirs.Count == 0) continue;

            var conversation = await _chat.StartOrGetDirectConversationAsync(sender, person.Email, ct);
            if (conversation.Succeeded)
                await _chat.SendMessageAsync(sender, conversation.Value,
                    new SendMessageRequest { Body = ChatBody(heading, theirs, isAdmin) }, ct);
        }
    }

    private static string ChatBody(string heading, IReadOnlyList<BirthdayPersonDto> people, bool withCardHint)
    {
        var lines = people.Select(p => $"• {p.Name} ({p.Kind}, turning {p.TurningAge})" +
                                       (string.IsNullOrWhiteSpace(p.Phone) ? "" : $" — {p.Phone}"));
        return heading + ":\n" + string.Join("\n", lines) +
               (withCardHint ? "\n\nOpen Birthdays in the menu to send a wish card on WhatsApp." : "");
    }

    /// <summary>Soonest first; on the same day staff before students, then by name.</summary>
    private static List<BirthdayPersonDto> Ordered(List<BirthdayPersonDto> list) =>
        list.OrderBy(p => p.DaysAway).ThenBy(p => p.Kind == "Student" ? 1 : 0).ThenBy(p => p.Name).ToList();

    /// <summary>29 Feb falls back to 28 Feb in a non-leap year.</summary>
    private static DateOnly Anniversary(DateOnly dob, int year) =>
        new(year, dob.Month, Math.Min(dob.Day, DateTime.DaysInMonth(year, dob.Month)));
}

/// Every 15 minutes, raises birthday alerts on the dashboard (day before and on
/// the day) and sends the chat reminders at 9 AM, 1 PM and 5 PM. Anything already
/// raised or sent for that day or reminder time is skipped.
/// </summary>
public class BirthdayNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BirthdayNotificationService> _logger;

    public BirthdayNotificationService(IServiceScopeFactory scopeFactory, ILogger<BirthdayNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var added = await scope.ServiceProvider.GetRequiredService<IBirthdayService>()
                    .RaiseTodayNotificationsAsync(stoppingToken);
                if (added > 0) _logger.LogInformation("Raised {Count} birthday alerts", added);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Birthday alert sweep failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
