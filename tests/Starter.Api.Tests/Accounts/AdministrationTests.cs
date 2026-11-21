using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Api.Tests.Infrastructure;
using Starter.Contracts;
using Starter.Identity.Data;

namespace Starter.Api.Tests.Accounts;

/// <summary>The Admin role: in the token, enforced by the API and by the identity server's administration pages.</summary>
public sealed class AdministrationTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Admin_stats_require_the_admin_role()
    {
        var (_, userTokens) = await SignedInUserAsync();
        var (_, adminTokens) = await AdminAsync();

        using var asUser = await GetAsync("/api/admin/stats", userTokens.AccessToken);
        using var asAdmin = await GetAsync("/api/admin/stats", adminTokens.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Administration_pages_are_only_for_administrators()
    {
        var user = await Fixture.CreateUserAsync();
        var (admin, _) = await AdminAsync();
        using var userBrowser = Fixture.Identity.CreateBrowser();
        using var adminBrowser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(userBrowser, user);
        await OAuthTestClient.SignInToAccountPagesAsync(adminBrowser, admin);

        using var denied = await userBrowser.GetAsync("/Admin/Users", Cancellation);
        using var allowed = await adminBrowser.GetAsync("/Admin/Users", Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("AccessDenied", denied.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Revoking_the_admin_role_ends_the_users_sessions()
    {
        var (admin, _) = await AdminAsync();
        var (target, targetTokens) = await AdminAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(browser, admin);
        var page = await browser.GetStringAsync($"/Admin/Users/Details/{target.Id}", Cancellation);

        using var response = await browser.PostAsync($"/Admin/Users/Details/{target.Id}?handler=RevokeAdmin", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page) }), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, targetTokens.RefreshToken!)).Error);
        var renewed = await OAuthTestClient.SignInAsync(Fixture.Identity, target);
        using var stats = await GetAsync("/api/admin/stats", renewed.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, stats.StatusCode);
    }

    [Fact]
    public async Task Administrator_can_lock_and_delete_another_account()
    {
        var (admin, _) = await AdminAsync();
        var (target, targetTokens) = await SignedInUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        await OAuthTestClient.SignInToAccountPagesAsync(browser, admin);

        var page = await browser.GetStringAsync($"/Admin/Users/Details/{target.Id}", Cancellation);
        using var locked = await browser.PostAsync($"/Admin/Users/Details/{target.Id}?handler=Lock", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page) }), Cancellation);
        Assert.Equal(HttpStatusCode.Redirect, locked.StatusCode);
        Assert.Equal("invalid_grant", (await OAuthTestClient.TryRefreshAsync(Fixture.Identity, targetTokens.RefreshToken!)).Error);

        page = await browser.GetStringAsync($"/Admin/Users/Details/{target.Id}", Cancellation);
        using var deleted = await browser.PostAsync($"/Admin/Users/Details/{target.Id}?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ConfirmEmail"] = target.Email,
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(page),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        await using var scope = Fixture.Identity.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(target.Id));
    }

    [Fact]
    public async Task Initial_administrators_from_configuration_are_promoted_by_the_migration_step()
    {
        var user = await Fixture.CreateUserAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(Fixture.IdentitySettings())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:InitialAdmins:0"] = user.Email })
            .Build();

        await Starter.Identity.Infrastructure.DatabaseSetup.MigrateAndSeedAsync(Fixture.Identity.Services, configuration, NullLogger.Instance, Cancellation);

        await using var scope = Fixture.Identity.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByIdAsync(user.Id))!, ApiConstants.AdminRole));
    }

    private async Task<(TestUser User, TokenSet Tokens)> AdminAsync()
    {
        var user = await Fixture.CreateUserAsync("Admin User");
        await using (var scope = Fixture.Identity.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(ApiConstants.AdminRole))
            {
                await roles.CreateAsync(new IdentityRole(ApiConstants.AdminRole));
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddToRoleAsync((await users.FindByIdAsync(user.Id))!, ApiConstants.AdminRole);
        }

        return (user, await OAuthTestClient.SignInAsync(Fixture.Identity, user));
    }
}
