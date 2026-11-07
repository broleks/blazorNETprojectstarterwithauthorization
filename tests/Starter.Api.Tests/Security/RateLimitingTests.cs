using System.Net;
using Starter.Api.Tests.Infrastructure;

namespace Starter.Api.Tests.Security;

public sealed class RateLimitingTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Api_returns_429_with_retry_after_when_a_user_sends_too_many_requests()
    {
        await using var limitedApi = new ApiFactory(Fixture, new() { ["RateLimiting:RequestsPerMinute"] = "3" });
        var (_, tokens) = await SignedInUserAsync();
        using var client = limitedApi.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me").WithBearer(tokens.AccessToken);
            using var response = await client.SendAsync(request, Cancellation);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(response.Headers.Contains("Retry-After"));
            }
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Account_forms_are_rate_limited()
    {
        await using var limitedIdentity = new IdentityFactory(Fixture, new() { ["RateLimiting:AccountPostsPerMinute"] = "2" });
        using var browser = limitedIdentity.CreateBrowser();
        var form = await browser.GetStringAsync("/Account/ForgotPassword", Cancellation);
        var token = OAuthTestClient.AntiforgeryToken(form);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await browser.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "nobody@example.test",
                ["__RequestVerificationToken"] = token,
            }), Cancellation);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
