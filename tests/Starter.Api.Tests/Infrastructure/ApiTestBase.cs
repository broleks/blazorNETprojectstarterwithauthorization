using System.Net.Http.Json;

namespace Starter.Api.Tests.Infrastructure;

/// <summary>Small helpers shared by API tests. Every test creates its own users, so tests never share data.</summary>
public abstract class ApiTestBase(StarterFixture fixture)
{
    protected StarterFixture Fixture { get; } = fixture;

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected async Task<(TestUser User, TokenSet Tokens)> SignedInUserAsync(string? displayName = null)
    {
        var user = await Fixture.CreateUserAsync(displayName);
        return (user, await OAuthTestClient.SignInAsync(Fixture.Identity, user));
    }

    protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var client = Fixture.Api.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.WithBearer(accessToken);
        }

        if (body is string json)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Cancellation);
    }

    protected Task<HttpResponseMessage> GetAsync(string path, string? accessToken) => SendAsync(HttpMethod.Get, path, accessToken);
}
