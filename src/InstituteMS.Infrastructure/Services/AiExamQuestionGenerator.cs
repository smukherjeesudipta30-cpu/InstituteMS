using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InstituteMS.Application.Common;
using InstituteMS.Application.TeacherExams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Generates teacher-exam MCQs by calling an AI provider chosen by
/// <c>Ai:Provider</c>: <c>Gemini</c> (default — Google's free tier at
/// aistudio.google.com) or <c>OpenRouter</c> (openrouter.ai — one key, many
/// models, several free ones whose IDs end in <c>:free</c>). Configured with
/// <c>Ai:ApiKey</c> (required) and <c>Ai:Model</c> (optional for Gemini,
/// required for OpenRouter) — same pattern as
/// <c>Firebase:ServiceAccountJsonPath</c>: read from configuration, absent in
/// source control, placed on the server directly.
/// </summary>
public class AiExamQuestionGenerator : IExamQuestionGenerator
{
    private const string DefaultModel = "gemini-3.6-flash";
    private const string ApiUrlTemplate =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";
    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly bool _useOpenRouter;
    private readonly ILogger<AiExamQuestionGenerator> _logger;

    public AiExamQuestionGenerator(
        HttpClient http, IConfiguration configuration, ILogger<AiExamQuestionGenerator> logger)
    {
        _http = http;
        _apiKey = configuration["Ai:ApiKey"];
        _useOpenRouter = string.Equals(configuration["Ai:Provider"], "OpenRouter", StringComparison.OrdinalIgnoreCase);
        // OpenRouter has no sensible fixed default: its free models come and
        // go, so the model is left for the server's configuration to name.
        _model = configuration["Ai:Model"] is { Length: > 0 } m ? m : _useOpenRouter ? "" : DefaultModel;
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

        // Generous: reasoning models spend part of this budget thinking before
        // they write, and a reply cut short loses whole questions.
        var response = await CallAsync(prompt, 16000, courseName, ct);
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

        return await CallAsync(prompt, 8000, courseName, ct);
    }

    /// <summary>
    /// The HTTP round trip shared by question and study-guide generation —
    /// same failure handling either way: no key configured, network failure,
    /// a non-2xx response, or an empty reply.
    /// </summary>
    private async Task<Result<string>> CallAsync(
        string prompt, int maxOutputTokens, string courseName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return Result<string>.Failure(
                "No AI provider key is configured on the server yet (Ai:ApiKey). " +
                "Everything else works; this will start working the moment a key is placed.",
                ResultStatus.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(_model))
        {
            return Result<string>.Failure(
                "Ai:Provider is OpenRouter but no Ai:Model is set on the server — name a model " +
                "from openrouter.ai/models (free ones end in \":free\").",
                ResultStatus.BadRequest);
        }

        // One retry, on a short delay — Gemini's own 503 "high demand" spikes
        // are usually seconds long, and scheduling a sitting is a deliberate,
        // occasional action worth a few extra seconds for, not a hot path
        // where a doubled request would ever pile up.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var result = await CallOnceAsync(prompt, maxOutputTokens, courseName, ct);
            if (result.Succeeded || result.Status != ResultStatus.TooManyRequests || attempt == 2)
                return result;

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        throw new UnreachableException();
    }

    private async Task<Result<string>> CallOnceAsync(
        string prompt, int maxOutputTokens, string courseName, CancellationToken ct)
    {
        using var request = _useOpenRouter
            ? BuildOpenRouterRequest(prompt, maxOutputTokens)
            : BuildGeminiRequest(prompt, maxOutputTokens);

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

            // Both are "try again shortly", not "something is misconfigured":
            // 503 is Google's own model being temporarily overloaded
            // ("currently experiencing high demand"); 429 is the free tier's
            // actual per-minute/per-day quota being hit — a real limit, not
            // an error, on a tier with no billing behind it to raise it.
            // Neither has anything to do with the key or model name, and
            // pointing someone at server configuration for either would send
            // them chasing a problem that doesn't exist.
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            {
                var reason = response.StatusCode == HttpStatusCode.ServiceUnavailable
                    ? "is temporarily overloaded (\"experiencing high demand\" on the provider's side)"
                    : "hit its free-tier rate limit for the moment";

                return Result<string>.Failure(
                    $"The AI provider {reason}. Already retried once; try scheduling again in a minute.",
                    ResultStatus.TooManyRequests);
            }

            return Result<string>.Failure(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "The AI provider rejected the key itself — check Ai:ApiKey on the server."
                    : $"The AI provider rejected the request ({(int)response.StatusCode} {response.StatusCode}) " +
                      "— check Ai:Model on the server.",
                ResultStatus.BadRequest);
        }

        string? text;
        if (_useOpenRouter)
        {
            var payload = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(cancellationToken: ct);
            // OpenRouter can answer 200 with an upstream failure in the body
            // (e.g. "provider_overloaded") — same "try again shortly" case as a 503.
            if (payload?.Error is { } error)
            {
                _logger.LogWarning("OpenRouter returned an error for {Course}: {Code} {Message}", courseName, error.Code, error.Message);
                return Result<string>.Failure(
                    "The AI provider is temporarily overloaded. Already retried once; try scheduling again in a minute.",
                    ResultStatus.TooManyRequests);
            }
            text = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        else
        {
            var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
            text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }

        return string.IsNullOrWhiteSpace(text)
            ? Result<string>.Failure(
                "The AI provider returned an empty response — the request may have been blocked by a " +
                "safety filter. Try again.", ResultStatus.BadRequest)
            : Result<string>.Success(text);
    }

    private HttpRequestMessage BuildGeminiRequest(string prompt, int maxOutputTokens)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, string.Format(ApiUrlTemplate, _model, _apiKey));
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new { maxOutputTokens }
        });
        return request;
    }

    /// <summary>OpenRouter speaks the OpenAI chat-completions format, key as a Bearer token.</summary>
    private HttpRequestMessage BuildOpenRouterRequest(string prompt, int maxOutputTokens)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Add("X-Title", "Avijan Institute");
        request.Content = JsonContent.Create(new
        {
            // Ai:Model may list several comma-separated models; OpenRouter
            // tries each in order, so one busy free model is not a failure.
            models = _model.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = maxOutputTokens
        });
        return request;
    }

    /// <summary>
    /// A model asked for JSON-only can still wrap it in a sentence or a
    /// ```json fence despite the instruction — this trims to the first
    /// '[' through the last ']' so a stray wrapper does not fail parsing.
    /// A reply cut off by the output limit (no closing ']') is closed after
    /// its last complete question, so the finished questions are kept rather
    /// than the whole reply thrown away.
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        if (start < 0) return text;

        var end = text.LastIndexOf(']');
        if (end > start) return text[start..(end + 1)];

        var lastComplete = text.LastIndexOf('}');
        return lastComplete > start ? text[start..(lastComplete + 1)] + "]" : text;
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class OpenRouterResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenRouterChoice>? Choices { get; set; }

        [JsonPropertyName("error")]
        public OpenRouterError? Error { get; set; }
    }

    private class OpenRouterError
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage? Message { get; set; }
    }

    private class OpenRouterMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private class GeminiPart
    {
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
