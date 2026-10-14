namespace InstituteMS.Application.Birthdays;

/// <summary>
/// Someone whose birthday falls in the window: a student (from the student's own
/// record) or a staff member (from their login account).
/// </summary>
/// <param name="Key">Stable id: "S-{registration}" for a student, "U-{user id}" for staff.</param>
/// <param name="Kind">Student, Teacher, Receptionist, Technical Staff, Editor, Admin or SuperAdmin.</param>
public record BirthdayPersonDto(
    string Key,
    string Kind,
    string? Name,
    string? Phone,
    string? RegistrationId,
    string? LoginId,
    DateOnly DateOfBirth,
    DateOnly NextBirthday,
    int DaysAway,
    int TurningAge);

public interface IBirthdayService
{
    /// <summary>Birthdays from today to <paramref name="days"/> days ahead, soonest first.</summary>
    Task<IReadOnlyList<BirthdayPersonDto>> GetUpcomingAsync(int days, CancellationToken ct = default);

    /// <summary>Adds one dashboard alert per person whose birthday is today; safe to call repeatedly.</summary>
    Task<int> RaiseTodayNotificationsAsync(CancellationToken ct = default);
}
