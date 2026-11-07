using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var sessionKey = state.User.FindFirst(ServerSideTicketStore.SessionKeyClaim)?.Value;
        if (sessionKey is null)
        {
            return new(ApiStatus.SessionExpired);
        }

        var token = await tokens.GetAccessTokenAsync(sessionKey, cancellationToken);
        switch (token.Status)
        {
            case AccessTokenStatus.SessionEnded:
                return new(ApiStatus.SessionExpired);
            case AccessTokenStatus.IdentityUnavailable:
                return new(ApiStatus.Unavailable);
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            return await ReadAsync<T>(response, method, path, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "API call {Method} {Path} failed: the API is not reachable", method, path);
            return new(ApiStatus.Unavailable);
        }
    }

    private async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            if (typeof(T) == typeof(bool))
            {
                return new(ApiStatus.Success, (T)(object)true);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return new(ApiStatus.Success, value);
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

        if (status is ApiStatus.Failed or ApiStatus.SessionExpired or ApiStatus.Forbidden)
        {
            logger.LogWarning("API call {Method} {Path} returned {StatusCode}", method, path, (int)response.StatusCode);
        }

        IReadOnlyDictionary<string, string[]>? errors = null;
        if (status == ApiStatus.ValidationFailed && response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
            errors = problem?.Errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        }

        return new(status, default, errors);
    }
}
