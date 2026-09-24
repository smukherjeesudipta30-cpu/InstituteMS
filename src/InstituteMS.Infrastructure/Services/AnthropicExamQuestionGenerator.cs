using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InstituteMS.Application.Common;
using InstituteMS.Application.TeacherExams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Generates teacher-exam MCQs by calling Anthropic's Messages API. Configured
/// with <c>Ai:ApiKey</c> (required) and <c>Ai:Model</c> (optional, defaults
/// below) — same pattern as <c>Firebase:ServiceAccountJsonPath</c>: read from
/// configuration, absent in source control, placed on the server directly.
/// </summary>
public class AnthropicExamQuestionGenerator : IExamQuestionGenerator
{
    private const string DefaultModel = "claude-sonnet-5";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<AnthropicExamQuestionGenerator> _logger;

    public AnthropicExamQuestionGenerator(
        HttpClient http, IConfiguration configuration, ILogger<AnthropicExamQuestionGenerator> logger)
    {
        _http = http;
        _apiKey = configuration["Ai:ApiKey"];
        _model = configuration["Ai:Model"] is { Length: > 0 } m ? m : DefaultModel;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<GeneratedQuestion>>> GenerateAsync(
        string courseName, int count, CancellationToken ct = default)
    {
        var prompt =
            $"Generate exactly {count} multiple-choice questions to test a TEACHER's own " +
            $"competency in the subject \"{courseName}\", suitable for an institute checking " +
            "that its instructor still knows the material well enough to teach it. Cover a " +
            "practical spread of the subject, not just trivia. Each question needs exactly four " +
            "options and exactly one correct answer. " +
            "Respond with ONLY a JSON array, no prose before or after, matching this shape: " +
            "[{\"question\":\"...\",\"optionA\":\"...\",\"optionB\":\"...\",\"optionC\":\"...\"," +
            "\"optionD\":\"...\",\"correct\":\"A\"}]. \"correct\" must be exactly one of A, B, C, D.";

        var response = await CallAsync(prompt, 4000, courseName, ct);
        if (!response.Succeeded)
            return Result<IReadOnlyList<GeneratedQuestion>>.Failure(response.Error!, response.Status);

        var json = ExtractJsonArray(response.Value!);
        List<GeneratedQuestionJson>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<GeneratedQuestionJson>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse AI question response for {Course}: {Text}", courseName, response.Value);
            return Result<IReadOnlyList<GeneratedQuestion>>.Failure(
                "The AI provider's response could not be read as questions. Try again.",
                ResultStatus.BadRequest);
        }

        if (raw is null || raw.Count == 0)
        {
            return Result<IReadOnlyList<GeneratedQuestion>>.Failure(
                "The AI provider did not return any questions. Try again.", ResultStatus.BadRequest);
        }

        var questions = raw
            .Where(q => !string.IsNullOrWhiteSpace(q.Question)
                && !string.IsNullOrWhiteSpace(q.OptionA) && !string.IsNullOrWhiteSpace(q.OptionB)
                && !string.IsNullOrWhiteSpace(q.OptionC) && !string.IsNullOrWhiteSpace(q.OptionD)
                && q.Correct is "A" or "B" or "C" or "D")
            .Select(q => new GeneratedQuestion(
                q.Question!, q.OptionA!, q.OptionB!, q.OptionC!, q.OptionD!, q.Correct!))
            .ToList();

        if (questions.Count == 0)
        {
            return Result<IReadOnlyList<GeneratedQuestion>>.Failure(
                "The AI provider's questions were not usable (missing options or answer). Try again.",
                ResultStatus.BadRequest);
        }

        return Result<IReadOnlyList<GeneratedQuestion>>.Success(questions);
    }

    public async Task<Result<string>> GenerateStudyGuideAsync(string courseName, CancellationToken ct = default)
    {
        var prompt =
            $"Write a concise study guide for a TEACHER preparing for their own competency exam " +
            $"in \"{courseName}\" at a computer training institute. Cover the key topics an " +
            "instructor teaching this subject should be confident in, with brief explanations — " +
            "practical and exam-relevant, not a full textbook. Use Markdown with headings and " +
            "bullet points. Respond with ONLY the guide itself, no preamble like \"Here is...\".";

        var response = await CallAsync(prompt, 2000, courseName, ct);
        return response;
    }

    /// <summary>
    /// The HTTP round trip shared by question and study-guide generation —
    /// same failure handling either way: no key configured, network failure,
    /// a non-2xx response, or an empty reply.
    /// </summary>
    private async Task<Result<string>> CallAsync(
        string prompt, int maxTokens, string courseName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return Result<string>.Failure(
                "No AI provider key is configured on the server yet (Ai:ApiKey). " +
                "Everything else works; this will start working the moment a key is placed.",
                ResultStatus.BadRequest);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = _model,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        });

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI request failed for {Course}", courseName);
            return Result<string>.Failure("Could not reach the AI provider. Try again shortly.", ResultStatus.BadRequest);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("AI request returned {Status} for {Course}: {Body}", response.StatusCode, courseName, body);
            return Result<string>.Failure(
                "The AI provider rejected the request — check Ai:ApiKey and Ai:Model on the server.",
                ResultStatus.BadRequest);
        }

        var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
        var text = payload?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        return string.IsNullOrWhiteSpace(text)
            ? Result<string>.Failure("The AI provider returned an empty response.", ResultStatus.BadRequest)
            : Result<string>.Success(text);
    }

    /// <summary>
    /// A model asked for JSON-only can still wrap it in a sentence or a
    /// ```json fence despite the instruction — this trims to the first
    /// '[' through the last ']' so a stray wrapper does not fail parsing.
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }
    }

    private class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class GeneratedQuestionJson
    {
        public string? Question { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? Correct { get; set; }
    }
}
