using InstituteMS.Application.Attendance;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.PreSchool;

/// <summary>One PreSchool staff member over the report's dates.</summary>
public record PreSchoolAttendanceStaffRowDto(
    int StaffId,
    string StaffName,
    string Role,
    int DaysPresent,
    int DaysAbsent,
    decimal TotalHours,
    // Marked in but never out: there are no hours to count for that day.
    int DaysMissingOut);

/// <summary>One person on one day.</summary>
public record PreSchoolAttendanceDayDto(
    DateOnly OnDate,
    int StaffId,
    string StaffName,
    string Role,
    DateTime? FirstIn,
    DateTime? LastOut,
    decimal? Hours,
    bool MissingOut,
    IReadOnlyList<AttendanceVisitDto> Visits);

public record PreSchoolAttendanceReportDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PreSchoolAttendanceStaffRowDto> Staff,
    IReadOnlyList<PreSchoolAttendanceDayDto> Days,
    decimal TotalHours);

/// <summary>
/// Attendance for the PreSchool's receptionist and teachers, worked the same way
/// as the Computer Centre's: a QR on the office screen or a button on their own
/// phone, inside the PreSchool's fence, confirmed by an emailed code. Reads and
/// writes PreSchool* tables only.
/// </summary>
public interface IPreSchoolAttendanceService
{
    Task<Result<AttendanceQrDto>> GetQrAsync(CancellationToken ct = default);
    Task<Result<AttendanceChallengeDto>> ScanAsync(string email, AttendanceScanRequest request, CancellationToken ct = default);
    Task<Result<AttendanceChallengeDto>> MarkAsync(string email, AttendanceMarkRequest request, CancellationToken ct = default);
    Task<Result<AttendanceScanResultDto>> ConfirmAsync(string email, AttendanceConfirmRequest request, CancellationToken ct = default);
    Task<Result<AttendanceChallengeDto>> ResendAsync(string email, int challengeId, CancellationToken ct = default);
    Task<Result<MyAttendanceDto>> GetMyDayAsync(string email, DateOnly? onDate, CancellationToken ct = default);
    Task<Result<PreSchoolAttendanceReportDto>> GetReportAsync(DateOnly from, DateOnly to, int? staffId, CancellationToken ct = default);
}
