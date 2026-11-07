using Starter.E2E.Tests.Infrastructure;

namespace Starter.E2E.Tests.Tests;

public sealed class AccountTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Display_name_can_be_changed_and_the_app_shows_the_new_name()
    {
        await SignedInAccountAsync(Page, "Old Name");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Account" }).ClickAsync();
        await Expect(Page.GetByTestId("profile-display-name")).ToHaveTextAsync("Old Name");

        await Page.GetByTestId("edit-profile").ClickAsync();
        await Page.GetByLabel("Display name").FillAsync("New Name");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Expect(Page.GetByTestId("alert-success")).ToContainTextAsync("Your profile has been updated");
        await Page.GetByTestId("return-to-app").ClickAsync();

        await Expect(Page.GetByTestId("profile-display-name")).ToHaveTextAsync("New Name");
        await Expect(Page.GetByTestId("user-display-name")).ToHaveTextAsync("New Name");
    }

    [Fact]
    public async Task Google_sign_in_is_not_offered_when_it_is_not_configured()
    {
        // The automated suite runs without Google credentials. With credentials configured, verify Google
        // manually as described in docs/google-sign-in.md.
        await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue with Google" })).ToHaveCountAsync(0);
    }
}
