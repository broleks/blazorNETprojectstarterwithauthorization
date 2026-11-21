using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starter.Api.Features.UserData;
using Starter.Api.Infrastructure;
using Starter.Api.Tests.Infrastructure;
using Starter.Contracts.Notes;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Accounts;

namespace Starter.Api.Tests.Accounts;

/// <summary>GDPR: data export and account deletion, across the identity server and the API.</summary>
public sealed class AccountDataTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Internal_account_endpoints_accept_only_the_identity_service_client()
    {
        var (user, tokens) = await SignedInUserAsync();
        var serviceToken = await OAuthTestClient.ServiceTokenAsync(Fixture.Identity);

        using var asUser = await GetAsync($"/api/internal/users/{user.Id}/export", tokens.AccessToken);
        using var asService = await GetAsync($"/api/internal/users/{user.Id}/export", serviceToken);
        using var serviceOnNotes = await GetAsync("/api/notes", serviceToken);

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asService.StatusCode);
        // The service token is not a user token: it cannot use the normal API.
        Assert.Equal(HttpStatusCode.Forbidden, serviceOnNotes.StatusCode);
    }

    [Fact]
    public async Task Web_client_cannot_use_the_client_credentials_grant()
    {
        using var client = Fixture.Identity.CreateClient();
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = StarterFixture.ClientId,
            ["client_secret"] = StarterFixture.ClientSecret,
            ["scope"] = "starter_api.accounts",
        }), Cancellation);

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Export_contains_the_account_and_the_users_notes_only()
    {
        var (user, tokens) = await SignedInUserAsync("Export Me");
        var (_, other) = await SignedInUserAsync();
        await CreateNoteAsync(tokens.AccessToken, "My exported note");
        await CreateNoteAsync(other.AccessToken, "Someone else's note");
        using var browser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(browser, user);
        var page = await browser.GetStringAsync("/Account/Manage/PersonalData", Cancellation);

        using var response = await browser.PostAsync("/Account/Manage/PersonalData?handler=Download", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page) }), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var json = await response.Content.ReadAsStringAsync(Cancellation);
        using var export = JsonDocument.Parse(json);
        Assert.Equal(user.Email, export.RootElement.GetProperty("account").GetProperty("email").GetString());
        Assert.Equal("Export Me", export.RootElement.GetProperty("account").GetProperty("displayName").GetString());
        Assert.Contains("My exported note", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Someone else's note", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_the_account_removes_it_ends_sessions_and_deletes_the_api_data()
    {
        var (user, tokens) = await SignedInUserAsync();
        await CreateNoteAsync(tokens.AccessToken, "To be deleted");
        using var browser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(browser, user);
        var page = await browser.GetStringAsync("/Account/Manage/DeleteAccount", Cancellation);

        using var response = await browser.PostAsync("/Account/Manage/DeleteAccount", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password"] = user.Password,
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccountDeleted", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        await using (var scope = Fixture.Identity.Services.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id));
        }

        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, tokens.RefreshToken!)).Error);
        Assert.Contains(Fixture.Emails.Sent, e => e.To == user.Email && e.Subject == "deleted");

        // The background processor deletes the API data (run once here instead of waiting for it).
        await Fixture.Identity.Services.GetRequiredService<PendingDeletionProcessor>().ProcessAsync(Cancellation);
        await using var apiScope = Fixture.Api.Services.CreateAsyncScope();
        Assert.False(await apiScope.ServiceProvider.GetRequiredService<AppDbContext>().Notes.AnyAsync(n => n.OwnerId == user.Id, Cancellation));
    }

    [Fact]
    public async Task Wrong_password_does_not_delete_the_account()
    {
        var user = await Fixture.CreateUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(browser, user);
        var page = await browser.GetStringAsync("/Account/Manage/DeleteAccount", Cancellation);

        using var response = await browser.PostAsync("/Account/Manage/DeleteAccount", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password"] = "not the password at all",
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = Fixture.Identity.Services.CreateAsyncScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id));
    }

    [Fact]
    public async Task Every_entity_with_an_owner_has_a_gdpr_handler()
    {
        await using var scope = Fixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handled = scope.ServiceProvider.GetServices<IUserDataHandler>().SelectMany(h => h.Entities).ToHashSet();

        var owned = db.Model.GetEntityTypes().Where(t => t.FindProperty("OwnerId") is not null).Select(t => t.ClrType);

        // If this fails: implement IUserDataHandler for the new feature and register it in Program.cs.
        Assert.All(owned, type => Assert.Contains(type, handled));
    }

    private async Task CreateNoteAsync(string token, string title)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/notes", token, new CreateNoteRequest { Title = title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
