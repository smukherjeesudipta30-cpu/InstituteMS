using System.Diagnostics;
using System.Text.Json;
using InstituteMS.Application.Common;

namespace InstituteMS.Api.Middleware;

/// <summary>
/// Turns any unhandled exception into the standard <see cref="ApiResponse{T}"/>
/// envelope with a traceId. Stack traces never reach the client — this is the
/// replacement for the legacy <c>customErrors mode="Off"</c> (defect D11).
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away — nothing to report.
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            _logger.LogError(ex, "Unhandled exception on {Method} {Path} (traceId {TraceId})",
                context.Request.Method, context.Request.Path, traceId);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response already started — cannot write the error envelope.");
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var body = new ApiResponse<object?>
            {
                Success = false,
                Message = "An unexpected error occurred. Please try again.",
                Errors = _env.IsDevelopment() ? [ex.Message] : [],
                TraceId = traceId
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}
