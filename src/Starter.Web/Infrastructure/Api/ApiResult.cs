namespace Starter.Web.Infrastructure.Api;

public enum ApiStatus
{
    Success,
    ValidationFailed,
    SessionExpired,
    Forbidden,
    NotFound,
    RateLimited,
    Unavailable,
    Failed,
}

/// <summary>Non-generic view of a result, used by UI components that only show errors.</summary>
public interface IApiResult
{
    ApiStatus Status { get; }
    bool Succeeded { get; }
    string Message { get; }
}

/// <summary>
/// Outcome of an API call, already translated into something the UI can show. Components never see HTTP
/// details, tokens or exception messages - only a status and a user-friendly message.
/// </summary>
public sealed record ApiResult<T>(ApiStatus Status, T? Value = default, IReadOnlyDictionary<string, string[]>? Errors = null) : IApiResult
{
    public bool Succeeded => Status == ApiStatus.Success;

    public string Message => Status switch
    {
        ApiStatus.Success => string.Empty,
        ApiStatus.ValidationFailed => "Please correct the highlighted fields.",
        ApiStatus.SessionExpired => "Your session has expired. Please sign in again.",
        ApiStatus.Forbidden => "You do not have permission to do that.",
        ApiStatus.NotFound => "We could not find what you were looking for. It may have been deleted.",
        ApiStatus.RateLimited => "You are going a bit fast. Wait a moment and try again.",
        ApiStatus.Unavailable => "The service is temporarily unavailable. Please try again in a moment.",
        _ => "Something went wrong. Please try again.",
    };
}
