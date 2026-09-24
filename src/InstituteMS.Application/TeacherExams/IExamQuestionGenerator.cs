namespace InstituteMS.Application.TeacherExams;

public record GeneratedQuestion(
    string QuestionText,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,

    /// <summary><c>A</c>, <c>B</c>, <c>C</c> or <c>D</c>.</summary>
    string CorrectOption);

/// <summary>
/// Generates a fresh set of multiple-choice questions for a course, on demand,
/// each time a sitting is scheduled — no reusable bank, so retakes never repeat
/// the same paper. Implemented against an AI provider; see
/// <c>InstituteMS.Infrastructure.Services.AnthropicExamQuestionGenerator</c>.
/// </summary>
public interface IExamQuestionGenerator
{
    /// <summary>
    /// Fails with a clear message (never throws) when no provider key is
    /// configured, so the rest of the exam portal stays usable/testable without
    /// one — see <c>Ai:ApiKey</c> in configuration.
    /// </summary>
    Task<Common.Result<IReadOnlyList<GeneratedQuestion>>> GenerateAsync(
        string courseName, int count, CancellationToken ct = default);

    /// <summary>
    /// A study guide for the teacher to prepare from before their sitting —
    /// same provider, generated alongside the questions. Fails the same clean
    /// way when no key is configured.
    /// </summary>
    Task<Common.Result<string>> GenerateStudyGuideAsync(string courseName, CancellationToken ct = default);
}
