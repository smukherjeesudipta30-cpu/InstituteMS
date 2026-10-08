using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Careers;

/// <summary>An open vacancy as the public sees it.</summary>
public record VacancyDto(
    int Id,
    string Title,
    string Branch,
    string? ShiftName,
    TimeOnly? ShiftStart,
    TimeOnly? ShiftEnd,
    string? WorkingDays,
    string Qualification,
    string? Experience,
    string? Salary,
    string? Description,
    int Openings,
    DateOnly? ClosesOn,
    bool IsActive,
    /// <summary>Admin only: how many have applied, and how many are still unread ("New").</summary>
    int Applications = 0,
    int NewApplications = 0);

public record VacancySaveRequest
{
    public string Title { get; init; } = "Receptionist";
    public string Branch { get; init; } = string.Empty;
    public string? ShiftName { get; init; }
    public TimeOnly? ShiftStart { get; init; }
    public TimeOnly? ShiftEnd { get; init; }
    public string? WorkingDays { get; init; }
    public string Qualification { get; init; } = string.Empty;
    public string? Experience { get; init; }
    public string? Salary { get; init; }
    public string? Description { get; init; }
    public int Openings { get; init; } = 1;
    public DateOnly? ClosesOn { get; init; }
    public bool IsActive { get; init; } = true;
}

public class VacancySaveRequestValidator : AbstractValidator<VacancySaveRequest>
{
    public VacancySaveRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Branch).NotEmpty().WithMessage("Choose the branch.").MaximumLength(100);
        RuleFor(x => x.Qualification).NotEmpty().WithMessage("Enter the academic qualification required.").MaximumLength(300);
        RuleFor(x => x.ShiftName).MaximumLength(50);
        RuleFor(x => x.WorkingDays).MaximumLength(100);
        RuleFor(x => x.Experience).MaximumLength(200);
        RuleFor(x => x.Salary).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Openings).InclusiveBetween(1, 50);
        RuleFor(x => x).Must(x => x.ShiftStart is null || x.ShiftEnd is null || x.ShiftEnd > x.ShiftStart)
            .WithMessage("The shift must end after it starts.");
    }
}

/// <summary>A file sent from the browser: name, type and Base64 content.</summary>
public record CareerUploadDto(string FileName, string ContentType, string Base64);

public record JobApplyRequest
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Qualification { get; init; } = string.Empty;
    public string? Experience { get; init; }
    public string? Address { get; init; }
    public string? Message { get; init; }
    public CareerUploadDto? Cv { get; init; }
    public CareerUploadDto? Photo { get; init; }
}

public class JobApplyRequestValidator : AbstractValidator<JobApplyRequest>
{
    public JobApplyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter your name.").MaximumLength(100);
        RuleFor(x => x.Phone).Matches("^[0-9]{10}$").WithMessage("Enter a 10-digit phone number.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(150);
        RuleFor(x => x.Qualification).NotEmpty().WithMessage("Enter your highest qualification.").MaximumLength(300);
        RuleFor(x => x.Experience).MaximumLength(300);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.Cv).NotNull().WithMessage("Attach your CV.");
        RuleFor(x => x.Photo).NotNull().WithMessage("Attach your photograph.");
    }
}

/// <summary>One application in Admin's list. The files are fetched separately.</summary>
public record JobApplicationDto(
    int Id,
    int VacancyId,
    string VacancyTitle,
    string Branch,
    string Name,
    string Phone,
    string? Email,
    string Qualification,
    string? Experience,
    string? Address,
    string? Message,
    string? CvFileName,
    string Status,
    string? AdminNote,
    DateTime AppliedOn,
    string? ReviewedBy,
    DateTime? ReviewedOn);

public record JobApplicationStatusRequest(string Status, string? AdminNote);

public record CareerFileDto(byte[] Content, string ContentType, string FileName);

public interface ICareerService
{
    /// <summary>Vacancies open today, for the public site.</summary>
    Task<IReadOnlyList<VacancyDto>> GetOpenAsync(CancellationToken ct = default);

    Task<Result<int>> ApplyAsync(int vacancyId, JobApplyRequest request, CancellationToken ct = default);

    /* ----------------------------------------------------------- Admin */
    Task<IReadOnlyList<VacancyDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result<VacancyDto>> SaveAsync(int? id, VacancySaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<JobApplicationDto>> GetApplicationsAsync(int? vacancyId, CancellationToken ct = default);
    Task<Result<JobApplicationDto>> SetStatusAsync(int id, JobApplicationStatusRequest request, string? actor, CancellationToken ct = default);
    Task<Result<CareerFileDto>> GetFileAsync(int applicationId, string kind, CancellationToken ct = default);
}
