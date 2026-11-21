using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Starter.E2E.Tests.Infrastructure;

namespace Starter.E2E.Tests.Tests;

public sealed class AccountDataTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task User_can_download_their_data_and_delete_their_account()
    {
        var account = await SignedInAccountAsync(Page, "Leaving User");
        await CreateNoteAsync(Page, "Note in my export");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Account", Exact = true }).ClickAsync();
        await Page.GetByTestId("personal-data").ClickAsync();

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("download-data").ClickAsync());
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await download.SaveAsAsync(file);
        var json = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        File.Delete(file); // personal data: do not keep it around
        using (var export = JsonDocument.Parse(json))
        {
            Assert.Equal(account.Email, export.RootElement.GetProperty("account").GetProperty("email").GetString());
        }

        Assert.Contains("Note in my export", json, StringComparison.Ordinal);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Delete my account" }).ClickAsync();
        await Page.GetByLabel("Enter your password to confirm").FillAsync(account.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete my account permanently" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Your account has been deleted" })).ToBeVisibleAsync();

        // The account no longer exists.
        var other = await NewPageAsync();
        await other.GotoAsync("/");
        await SubmitSignInAsync(other, account.Email, account.Password);
        await Expect(other.GetByTestId("login-error")).ToHaveTextAsync("The email or password is incorrect.");
    }

    [Fact]
    public async Task Regular_users_see_no_administration()
    {
        await SignedInAccountAsync(Page);

        await Expect(Page.GetByTestId("nav-admin")).ToHaveCountAsync(0);
        await Page.GotoAsync("/admin");
        await Expect(Page.GetByTestId("access-denied")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Administrator_sees_statistics_and_the_user_list()
    {
        // Needs E2E_SQL: the test promotes its own account directly in the test database (no backdoor endpoint).
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Settings.CleanupConnectionString), "E2E_SQL is not set");
        var account = await SignedInAccountAsync(Page, "Admin Tester");
        await GrantAdminAsync(account.Email);

        // Roles are in the tokens: sign in again to get them.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await SignInAsync(Page, account);
        await Page.GetByTestId("nav-admin").ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Administration" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("admin-total-notes")).ToHaveTextAsync(new Regex("^[0-9]+$"));
        await Page.GetByTestId("manage-users").ClickAsync();
        await Page.GetByRole(AriaRole.Searchbox).Or(Page.GetByLabel("Search users")).FillAsync(account.Email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Expect(Page.GetByTestId("users-table")).ToContainTextAsync(account.Email);
    }

    private async Task GrantAdminAsync(string email)
    {
        await using var connection = new SqlConnection(Settings.CleanupConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [identity].AspNetUserRoles (UserId, RoleId)
            SELECT u.Id, r.Id FROM [identity].AspNetUsers u CROSS JOIN [identity].AspNetRoles r
            WHERE u.NormalizedEmail = @email AND r.NormalizedName = 'ADMIN';
            UPDATE [identity].AspNetUsers SET SecurityStamp = NEWID() WHERE NormalizedEmail = @email;
            """;
        command.Parameters.AddWithValue("@email", email.ToUpperInvariant());
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }
}
