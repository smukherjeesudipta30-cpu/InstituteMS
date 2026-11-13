using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.PreSchool;

public record PreSchoolExamDto(int Id, string Name, DateOnly? HeldOn, int SortOrder, bool IsActive);

public record PreSchoolExamSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public DateOnly? HeldOn { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public class PreSchoolExamSaveRequestValidator : AbstractValidator<PreSchoolExamSaveRequest>
{
    public PreSchoolExamSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the exam a name.").MaximumLength(80);
    }
}

public record PreSchoolMarkSubjectDto(int Id, string Name, int MaxMarks);

/// <summary>One subject's mark: null while not entered; absent counts as nothing.</summary>
public record PreSchoolMarkCellDto(int SubjectId, decimal? Marks, bool IsAbsent);

public record PreSchoolMarkRowDto(
    int StudentId,
    string AdmissionCode,
    string ChildName,
    IReadOnlyList<PreSchoolMarkCellDto> Marks,
    decimal Total,
    // The full marks of the subjects entered so far, so a half-filled row reads fairly.
    decimal OutOf,
    decimal? Percent,
    string? Grade);

/// <summary>The marks sheet for one exam and one class: its subjects down the top, its children down the side.</summary>
public record PreSchoolMarkSheetDto(
    int ExamId,
    string ExamName,
    int ClassId,
    string ClassName,
    IReadOnlyList<PreSchoolMarkSubjectDto> Subjects,
    IReadOnlyList<PreSchoolMarkRowDto> Rows);

public record PreSchoolMarkEntry(int StudentId, int SubjectId, decimal? Marks, bool IsAbsent);

public record PreSchoolMarkSaveRequest
{
    public int ExamId { get; init; }
    public int ClassId { get; init; }
    public IReadOnlyList<PreSchoolMarkEntry> Entries { get; init; } = [];
}

public class PreSchoolMarkSaveRequestValidator : AbstractValidator<PreSchoolMarkSaveRequest>
{
    public PreSchoolMarkSaveRequestValidator()
    {
        RuleFor(x => x.ExamId).GreaterThan(0).WithMessage("Pick an exam.");
        RuleFor(x => x.ClassId).GreaterThan(0).WithMessage("Pick a class.");
        RuleFor(x => x.Entries.Count).LessThanOrEqualTo(5000);
        RuleForEach(x => x.Entries).ChildRules(e =>
            e.RuleFor(m => m.Marks).GreaterThanOrEqualTo(0).When(m => m.Marks.HasValue).WithMessage("Marks cannot be negative."));
    }
}

/// <summary>Offline exam marks for the PreSchool. No report card or ID card is made from them.</summary>
public interface IPreSchoolMarkService
{
    Task<IReadOnlyList<PreSchoolExamDto>> ExamsAsync(CancellationToken ct = default);
    Task<Result<PreSchoolExamDto>> SaveExamAsync(int? id, PreSchoolExamSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolMarkSheetDto>> SheetAsync(int examId, int classId, CancellationToken ct = default);
    Task<Result<PreSchoolMarkSheetDto>> SaveAsync(PreSchoolMarkSaveRequest request, string? actor, CancellationToken ct = default);
}
