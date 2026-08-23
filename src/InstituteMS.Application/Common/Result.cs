namespace InstituteMS.Application.Common;

/// <summary>
/// Service-layer outcome. Lets services report business failures without
/// throwing, so controllers can map them to the right HTTP status.
/// </summary>
public readonly record struct Result<T>(bool Succeeded, T? Value, string? Error, ResultStatus Status)
{
    public static Result<T> Success(T value) => new(true, value, null, ResultStatus.Ok);

    public static Result<T> Failure(string error, ResultStatus status = ResultStatus.BadRequest) =>
        new(false, default, error, status);
}

public enum ResultStatus
{
    Ok = 0,
    BadRequest = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    TooManyRequests = 6
}
