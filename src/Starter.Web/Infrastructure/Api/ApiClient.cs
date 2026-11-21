using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Starter.Observability;
using Starter.Web.Infrastructure.Authentication;
using Starter.Web.Infrastructure.Sessions;

namespace Starter.Web.Infrastructure.Api;

/// <summary>
/// Calls Starter.Api on behalf of the signed-in user.
///
/// Registered as scoped, so in Blazor Server each circuit (one browser tab of one user) has its own instance.
/// The user is identified through <see cref="AuthenticationStateProvider"/> - which works both during the
/// initial HTTP request and later inside the circuit - and the token is attached to each individual request
/// message. The shared HttpClient never carries a default Authorization header, so tokens cannot leak
/// between users.
///
/// Every call is logged (category Api) with its own trace id. The trace id travels to the API in the
/// W3C <c>traceparent</c> header, so the Web and API log entries of one call share <c>trace.id</c>, and it is
/// returned in <see cref="ApiResult{T}.ReferenceId"/> so the UI can show it to the user when something fails.
/// </summary>
public sealed class ApiClient(
    IHttpClientFactory httpClientFactory,
    AuthenticationStateProvider authenticationStateProvider,
    SessionTokenService tokens,
    ILogger<ApiClient> logger)
{
    public const string HttpClientName = "StarterApi";

    public Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, content: null, cancellationToken);

    public Task<ApiResult<T>> PostAsync<T>(string path, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, JsonContent.Create(body), cancellationToken);

    public Task<ApiResult<T>> PutAsync<T>(string path, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, JsonContent.Create(body), cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, path, content: null, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        // A new root trace per API call. Inside a Blazor circuit the ambient activity may belong to the long-lived
        // connection, which would give all calls of a tab the same trace id.
        var previous = Activity.Current;
        Activity.Current = null;
        using var activity = new Activity("Starter.Web.ApiCall").Start();
        var started = Stopwatch.GetTimestamp();
        var log = new CallLog(method, path, activity.TraceId.ToString());

        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            log.UserId = state.User.FindFirst("sub")?.Value;
            var sessionKey = state.User.FindFirst(ServerSideTicketStore.SessionKeyClaim)?.Value;
            if (sessionKey is null)
            {
                return log.Result<T>(ApiStatus.SessionExpired, reason: "no_session");
            }

            log.SessionId = ActivityLogExtensions.HashForLogs(sessionKey);
            var token = await tokens.GetAccessTokenAsync(sessionKey, cancellationToken);
            switch (token.Status)
            {
                case AccessTokenStatus.SessionEnded:
                    return log.Result<T>(ApiStatus.SessionExpired, reason: "session_ended");
                case AccessTokenStatus.IdentityUnavailable:
                    return log.Result<T>(ApiStatus.Unavailable, reason: "identity_server_unavailable");
            }

            using var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken);
                return await ReadAsync<T>(response, log, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Connection refused, DNS, TLS/certificate problems, timeouts: the exact cause is in the exception.
                log.Exception = ex;
                return log.Result<T>(ApiStatus.Unavailable, reason: ex is TaskCanceledException ? "timeout" : "connection_failed");
            }
        }
        finally
        {
            log.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Write(log);
            activity.Stop();
            Activity.Current = previous;
        }
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CallLog log, CancellationToken cancellationToken)
    {
        log.StatusCode = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            if (typeof(T) == typeof(bool))
            {
                return log.Result(ApiStatus.Success, (T)(object)true);
            }

            return log.Result(ApiStatus.Success, await response.Content.ReadFromJsonAsync<T>(cancellationToken));
        }

        var status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => ApiStatus.ValidationFailed,
            HttpStatusCode.Unauthorized => ApiStatus.SessionExpired,
            HttpStatusCode.Forbidden => ApiStatus.Forbidden,
            HttpStatusCode.NotFound => ApiStatus.NotFound,
            HttpStatusCode.TooManyRequests => ApiStatus.RateLimited,
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout => ApiStatus.Unavailable,
            _ => ApiStatus.Failed,
        };

        IReadOnlyDictionary<string, string[]>? errors = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
            log.ProblemTitle = problem?.Title;
            if (problem?.Errors.Count > 0)
            {
                errors = problem.Errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
                log.ValidationErrors = string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
            }
        }

        return log.Result<T>(status, errors: errors);
    }

    private void Write(CallLog log)
    {
        var details = new Dictionary<string, object?>
        {
            ["HttpMethod"] = log.Method.Method,
            ["Path"] = log.Path,
            ["StatusCode"] = log.StatusCode,
            ["ApiStatus"] = log.Status.ToString(),
            [LogProperties.DurationMs] = log.DurationMs,
            [LogProperties.SessionId] = log.SessionId,
            ["Reason"] = log.Reason,
            ["ProblemTitle"] = log.ProblemTitle,
            ["ValidationErrors"] = log.ValidationErrors,
        };

        // Expected outcomes (not found, validation, expired session) are warnings; failures of the system are errors.
        var level = log.Status switch
        {
            ApiStatus.Success => LogLevel.Information,
            ApiStatus.Unavailable or ApiStatus.Failed => LogLevel.Error,
            _ => LogLevel.Warning,
        };

        logger.LogActivity(LogCategories.Api, "api.call",
            log.Status == ApiStatus.Success ? EventOutcome.Success : EventOutcome.Failure,
            details, log.UserId, level, log.Exception);
    }

    /// <summary>What is known about one call; written to the log when the call ends.</summary>
    private sealed class CallLog(HttpMethod method, string path, string traceId)
    {
        public HttpMethod Method { get; } = method;
        public string Path { get; } = path;
        public string TraceId { get; } = traceId;
        public string? UserId { get; set; }
        public string? SessionId { get; set; }
        public int? StatusCode { get; set; }
        public ApiStatus Status { get; private set; }
        public string? Reason { get; private set; }
        public string? ProblemTitle { get; set; }
        public string? ValidationErrors { get; set; }
        public long DurationMs { get; set; }
        public Exception? Exception { get; set; }

        public ApiResult<T> Result<T>(ApiStatus status, T? value = default, IReadOnlyDictionary<string, string[]>? errors = null, string? reason = null)
        {
            Status = status;
            Reason = reason;
            return new ApiResult<T>(status, value, errors, status == ApiStatus.Success ? null : TraceId);
        }
    }
}
