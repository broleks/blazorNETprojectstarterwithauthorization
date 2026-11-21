using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Starter.Api.Tests.Infrastructure;
using Starter.Contracts.Notes;

namespace Starter.Api.Tests.Logging;

/// <summary>
/// The activity log (docs/logging.md): what is written for API calls and security events, that it can be switched
/// off per category, and that secrets never end up in it. Reads the real ECS JSON files the applications write.
/// </summary>
public sealed class ActivityLoggingTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Api_call_is_logged_with_user_route_status_and_the_trace_id_returned_to_the_client()
    {
        var (user, tokens) = await SignedInUserAsync();

        using var response = await GetAsync($"/api/notes/{Guid.NewGuid()}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var traceId = TraceIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation));
        var entry = Assert.Single(await LogFiles.WaitForAsync(Fixture.Api.LogFolder, "api",
            e => e.Get("event.action") == "api.request" && e.Get("trace.id") == traceId));
        Assert.Equal("failure", entry.Get("event.outcome"));
        Assert.Equal(user.Id, entry.Get("user.id"));
        Assert.Equal("api", entry.Get("service.name"));
        Assert.Equal("404", entry.Get("http.response.status_code"));
        Assert.Equal("/api/notes/{id:guid}", entry.Get("labels.Route"));
        Assert.Equal("Api", entry.Get("labels.LogCategory"));
    }

    [Fact]
    public async Task Validation_errors_are_described_without_the_submitted_values()
    {
        var (_, tokens) = await SignedInUserAsync();
        var secretContent = "private-" + Guid.NewGuid().ToString("N") + new string('x', NoteLimits.ContentMaxLength);

        using var response = await SendAsync(HttpMethod.Post, "/api/notes", tokens.AccessToken,
            new CreateNoteRequest { Title = "Valid title", Content = secretContent });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var traceId = TraceIdOf(await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation));
        var entry = Assert.Single(await LogFiles.WaitForAsync(Fixture.Api.LogFolder, "api", e => e.Get("trace.id") == traceId && e.Get("event.action") == "api.request"));
        Assert.Contains("Content: The note can be at most", entry.Get("labels.ValidationErrors") ?? entry.Get("metadata.ValidationErrors"), StringComparison.Ordinal);
        Assert.DoesNotContain(secretContent[..20], LogFiles.AllText(Fixture.Api.LogFolder), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_sign_in_is_logged_with_reason_and_masked_email_but_never_the_password()
    {
        var user = await Fixture.CreateUserAsync();
        const string wrongPassword = "Wrong-Password-Marker-123";
        using var browser = Fixture.Identity.CreateBrowser();
        var form = await browser.GetStringAsync("/Account/Login", Cancellation);

        using var _ = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email,
            ["Input.Password"] = wrongPassword,
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);

        var entry = Assert.Single(await LogFiles.WaitForAsync(Fixture.Identity.LogFolder, "identity",
            e => e.Get("event.action") == "auth.sign_in" && e.Get("user.id") == user.Id));
        Assert.Equal("failure", entry.Get("event.outcome"));
        Assert.Equal("wrong_password", entry.Get("labels.Reason"));
        Assert.Equal("u***@example.test", entry.Get("labels.Email"));

        var everything = LogFiles.AllText(Fixture.Identity.LogFolder);
        Assert.DoesNotContain(wrongPassword, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Password, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_through_the_real_flow_logs_security_events_and_no_tokens()
    {
        var (user, tokens) = await SignedInUserAsync();

        var events = await LogFiles.WaitForAsync(Fixture.Identity.LogFolder, "identity",
            e => e.Get("user.id") == user.Id && e.Get("labels.LogCategory") == "Security", expected: 2);
        Assert.Contains(events, e => e.Get("event.action") == "auth.sign_in" && e.Get("event.outcome") == "success");
        Assert.Contains(events, e => e.Get("event.action") == "auth.token" && e.Get("event.outcome") == "success");
        var everything = LogFiles.AllText(Fixture.Identity.LogFolder) + LogFiles.AllText(Fixture.Api.LogFolder);
        Assert.DoesNotContain(tokens.AccessToken, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.RefreshToken!, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_category_can_be_switched_off()
    {
        await using var api = new ApiFactory(Fixture, new() { ["ActivityLogging:Categories:Api"] = "false" });
        var (user, tokens) = await SignedInUserAsync();
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me").WithBearer(tokens.AccessToken);

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await LogFiles.WaitForAsync(api.LogFolder, "api", e => e.Get("user.id") == user.Id));
    }

    [Fact]
    public async Task Master_switch_turns_all_stored_logging_off()
    {
        await using var api = new ApiFactory(Fixture, new() { ["ActivityLogging:Enabled"] = "false" });
        var (_, tokens) = await SignedInUserAsync();
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/notes?pageSize=1000").WithBearer(tokens.AccessToken);

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LogFiles.WaitForAsync(api.LogFolder, "api", _ => true));
    }

    // ProblemDetails.traceId is the W3C id "00-<trace id>-<span id>-01"; logs store the 32-character trace id.
    private static string TraceIdOf(JsonElement problem) => problem.GetProperty("traceId").GetString()!.Split('-')[1];
}
