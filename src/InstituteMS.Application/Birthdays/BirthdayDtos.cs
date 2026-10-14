using InstituteMS.Application.Common;

namespace InstituteMS.Application.Birthdays;

/// <summary>
/// Someone whose birthday falls in the window: a student (from the student's own
/// record) or a staff member (from their login account).
/// </summary>
/// <param name="Key">Stable id: "S-{registration}" for a student, "U-{user id}" for staff.</param>
/// <param name="Kind">Student, Teacher, Receptionist, Technical Staff, Editor, Admin, SuperAdmin or Proprietor.</param>
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

/// <summary>The Proprietor's details for the birthday alert (they have no login account).</summary>
public record ProprietorProfileDto(string Name, DateOnly? DateOfBirth, string? Phone);

public interface IBirthdayService
{
    Task<ProprietorProfileDto> GetProprietorAsync(CancellationToken ct = default);
    Task<Result<ProprietorProfileDto>> SaveProprietorAsync(ProprietorProfileDto request, string? actor, CancellationToken ct = default);

    /// <summary>Birthdays from today to <paramref name="days"/> days ahead, soonest first.</summary>
    Task<IReadOnlyList<BirthdayPersonDto>> GetUpcomingAsync(int days, CancellationToken ct = default);

    /// <summary>Adds one dashboard alert per person whose birthday is today; safe to call repeatedly.</summary>
    Task<int> RaiseTodayNotificationsAsync(CancellationToken ct = default);
}
