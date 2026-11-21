using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Starter.Contracts;

namespace Starter.Identity.Infrastructure.Accounts;

/// <summary>
/// Calls the API's internal account endpoints as the identity server's service client (OAuth client credentials,
/// scope <c>starter_api.accounts</c>). The token belongs to the service, not to a user, so caching it in this
/// singleton is safe; it is renewed shortly before it expires.
/// </summary>
public sealed class ApiServiceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenIddictSettings> openIddict,
    IConfiguration configuration,
    TimeProvider clock) : IDisposable
{
    public const string HttpClientName = "StarterApiService";
    public const string ServiceClientId = "starter-identity-service";

    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;

    /// <summary>The user's data from every API feature (one section per feature).</summary>
    public async Task<JsonElement> ExportUserDataAsync(string userId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"api/internal/users/{Uri.EscapeDataString(userId)}/export", cancellationToken);
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>Deletes the user's data in every API feature. Safe to repeat.</summary>
    public async Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"api/internal/users/{Uri.EscapeDataString(userId)}", cancellationToken);
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => tokenLock.Dispose();

    private HttpClient Client => httpClientFactory.CreateClient(HttpClientName);

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        return request;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (token is not null && tokenExpires - clock.GetUtcNow() > TimeSpan.FromMinutes(1))
            {
                return token;
            }

            var secret = openIddict.Value.Clients.TryGetValue(ServiceClientId, out var client) ? client.ClientSecret : null;
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException($"OpenIddict:Clients:{ServiceClientId}:ClientSecret is not configured.");
            }

            var issuer = configuration["Authentication:Issuer"] ?? throw new InvalidOperationException("Authentication:Issuer is not configured.");
            using var response = await httpClientFactory.CreateClient(HttpClientName + ".Token").PostAsync(
                new Uri(new Uri(issuer), "connect/token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = ServiceClientId,
                    ["client_secret"] = secret,
                    ["scope"] = ApiConstants.AccountsScope,
                }),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            token = json.RootElement.GetProperty("access_token").GetString();
            tokenExpires = clock.GetUtcNow().AddSeconds(json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300);
            return token!;
        }
        finally
        {
            tokenLock.Release();
        }
    }
}
