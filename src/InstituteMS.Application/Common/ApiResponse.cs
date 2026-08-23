namespace InstituteMS.Application.Common;

/// <summary>
/// The single response envelope every endpoint returns (MIGRATION_PLAN §3.1).
/// The Angular client and the future APK both depend on this shape, so it must
/// stay stable across versions.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string? TraceId { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, IReadOnlyList<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}

/// <summary>Non-generic helper for endpoints with no payload.</summary>
public static class ApiResponse
{
    public static ApiResponse<object?> Ok(string? message = null) =>
        new() { Success = true, Data = null, Message = message };

    public static ApiResponse<object?> Fail(string message, IReadOnlyList<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}
