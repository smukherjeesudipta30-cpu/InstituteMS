using System.Security.Cryptography;
using InstituteMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Hands a file the page made itself (a PDF admit card, a certificate, a CSV
/// report) back to the browser from an ordinary https address.
/// </summary>
/// <remarks>
/// Phone browsers such as Samsung Internet refuse to download the in-memory
/// <c>blob:</c> links a page creates ("Only http or https URLs can be
/// downloaded"). On a phone the website therefore posts the file here and opens
/// the address returned. Files are held in memory for five minutes under a
/// random, unguessable code and are never written to disk.
/// </remarks>
[Route("api/v1/downloads")]
[Tags("Downloads")]
[AllowAnonymous]
public class DownloadsController : ApiControllerBase
{
    private const int MaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;

    public DownloadsController(IMemoryCache cache) => _cache = cache;

    public record DownloadRequest(string FileName, string ContentType, string Base64);

    private sealed record HeldFile(byte[] Content, string ContentType, string FileName);

    /// <summary>Holds a file for five minutes and returns the code to fetch it with.</summary>
    [HttpPost]
    [EnableRateLimiting("auth")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public IActionResult Hold([FromBody] DownloadRequest request)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.Base64 ?? string.Empty); }
        catch (FormatException) { return Envelope(Result<string>.Failure("The file could not be read.")); }

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            return Envelope(Result<string>.Failure("The file is empty or too large to download this way."));

        var name = Path.GetFileName(request.FileName ?? "download");
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        var type = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType;

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _cache.Set("download:" + code, new HeldFile(bytes, type, name), Lifetime);
        return Envelope(Result<string>.Success(code));
    }

    /// <summary>The held file, as a normal download.</summary>
    [HttpGet("{code}")]
    public IActionResult Fetch(string code)
    {
        if (!_cache.TryGetValue("download:" + code, out HeldFile? file) || file is null)
            return NotFound("This download has expired. Please go back and try again.");

        return File(file.Content, file.ContentType, file.FileName);
    }
}
