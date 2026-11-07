using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Starter.Api.Tests.Infrastructure;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;

namespace Starter.Api.Tests.Security;

/// <summary>
/// Sign-out and credential changes end sessions by revoking refresh tokens. Already issued access tokens are
/// self-contained JWTs and stay valid until they expire (5 minutes by default) - see docs/security.md.
/// </summary>
public sealed class SessionRevocationTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Refresh_token_rotates_and_new_access_token_works()
    {
        var (_, tokens) = await SignedInUserAsync();

        var refreshed = await OAuthTestClient.RefreshAsync(Fixture.Identity, tokens.RefreshToken!);

        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
        using var response = await GetAsync("/api/me", refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Revoked_refresh_token_cannot_be_used()
    {
        var (_, tokens) = await SignedInUserAsync();

        await OAuthTestClient.RevokeAsync(Fixture.Identity, tokens.RefreshToken!);

        var (status, error) = await OAuthTestClient.TryRefreshAsync(Fixture.Identity, tokens.RefreshToken!);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_grant", error);
    }

    [Fact]
    public async Task Access_token_stays_valid_until_expiry_after_revocation_documented_limitation()
    {
        var (_, tokens) = await SignedInUserAsync();

        await OAuthTestClient.RevokeAsync(Fixture.Identity, tokens.RefreshToken!);

        // The API validates JWTs locally and does not ask the identity server about revocation.
        using var response = await GetAsync("/api/me", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Password_change_ends_all_sessions()
    {
        var (user, firstDevice) = await SignedInUserAsync();
        var secondDevice = await OAuthTestClient.SignInAsync(Fixture.Identity, user);

        await using (var scope = Fixture.Identity.Services.CreateAsyncScope())
        {
            // The same steps the "Change password" page performs.
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var account = await users.FindByIdAsync(user.Id);
            Assert.True((await users.ChangePasswordAsync(account!, user.Password, "a brand new password")).Succeeded);
            await scope.ServiceProvider.GetRequiredService<AccountSecurity>().RevokeAllSessionsAsync(account!, Cancellation);
        }

        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, firstDevice.RefreshToken!)).Error);
        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, secondDevice.RefreshToken!)).Error);
    }

    [Fact]
    public async Task Security_stamp_change_alone_invalidates_refresh_tokens()
    {
        // Defence in depth: even without explicit revocation, a refresh token issued before a security
        // stamp change (password reset, email change...) is refused by the token endpoint.
        var (user, tokens) = await SignedInUserAsync();

        await using (var scope = Fixture.Identity.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByIdAsync(user.Id))!);
        }

        var (status, error) = await OAuthTestClient.TryRefreshAsync(Fixture.Identity, tokens.RefreshToken!);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_grant", error);
    }

    [Fact]
    public async Task Deleted_account_cannot_refresh()
    {
        var (user, tokens) = await SignedInUserAsync();

        await using (var scope = Fixture.Identity.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.DeleteAsync((await users.FindByIdAsync(user.Id))!);
        }

        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, tokens.RefreshToken!)).Error);
    }
}
