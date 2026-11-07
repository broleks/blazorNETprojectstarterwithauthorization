using System.Text.RegularExpressions;
using Starter.E2E.Tests.Infrastructure;

namespace Starter.E2E.Tests.Tests;

public sealed class AuthenticationTests(PlaywrightFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Anonymous_user_is_sent_to_sign_in()
    {
        await Page.GotoAsync("/notes");

        await Expect(Page).ToHaveURLAsync(new Regex("^" + Regex.Escape(Settings.IdentityUrl + "Account/Login")));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Registration_with_display_name_asks_to_confirm_email_without_showing_the_link()
    {
        var account = NewAccount("Grace Hopper");

        await RegisterAsync(Page, account);

        await Expect(Page.GetByText("We sent a confirmation link")).ToBeVisibleAsync();
        await Expect(Page.Locator("a[href*='ConfirmEmail']")).ToHaveCountAsync(0);
        // Not confirmed yet: signing in is refused with a hint.
        await Page.GotoAsync("/");
        await SubmitSignInAsync(Page, account.Email, account.Password);
        await Expect(Page.GetByTestId("login-error")).ToContainTextAsync("Confirm your email address");
    }

    [Fact]
    public async Task Email_is_confirmed_through_the_test_inbox_and_the_user_lands_in_the_app()
    {
        var account = NewAccount("Grace Hopper");
        await RegisterAsync(Page, account);

        await ConfirmEmailAsync(Page, account);
        await SubmitSignInAsync(Page, account.Email, account.Password);

        await ExpectDashboardAsync(Page);
        await Expect(Page).ToHaveURLAsync(Settings.WebUrl.ToString());
        await Expect(Page.GetByTestId("user-display-name")).ToHaveTextAsync("Grace Hopper");
    }

    [Fact]
    public async Task Sign_in_with_valid_credentials_on_a_new_device()
    {
        var account = await SignedInAccountAsync(Page);
        var otherDevice = await NewPageAsync();

        await SignInAsync(otherDevice, account);

        await Expect(otherDevice.GetByTestId("user-display-name")).ToHaveTextAsync(account.DisplayName);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var account = await SignedInAccountAsync(Page);
        var otherDevice = await NewPageAsync();
        await otherDevice.GotoAsync("/");

        await SubmitSignInAsync(otherDevice, account.Email, "this is not the password");

        await Expect(otherDevice.GetByTestId("login-error")).ToHaveTextAsync("The email or password is incorrect.");
        await Expect(otherDevice).ToHaveURLAsync(new Regex("Account/Login"));
    }

    [Fact]
    public async Task Sign_out_ends_the_session_and_protected_pages_require_sign_in_again()
    {
        await SignedInAccountAsync(Page);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You have signed out" })).ToBeVisibleAsync();
        await Page.GotoAsync("/notes");
        // The identity server session ended too, so the sign-in form is shown (no silent sign-in).
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("Account/Login"));
    }

    [Fact]
    public async Task Password_can_be_reset_through_the_emailed_link()
    {
        var account = await SignedInAccountAsync(Page);
        var device = await NewPageAsync();
        await device.GotoAsync("/");

        await device.GetByRole(AriaRole.Link, new() { Name = "Forgot your password?" }).ClickAsync();
        await device.GetByLabel("Email").FillAsync(account.Email);
        await device.GetByRole(AriaRole.Button, new() { Name = "Send reset link" }).ClickAsync();
        await Expect(device.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();

        var link = await Mailbox.WaitForLinkAsync(account.Email, "Reset your password");
        await device.GotoAsync(link);
        const string newPassword = "my brand new e2e password";
        await device.GetByLabel("Email").FillAsync(account.Email);
        await device.GetByLabel("New password", new() { Exact = true }).FillAsync(newPassword);
        await device.GetByLabel("Confirm new password").FillAsync(newPassword);
        await device.GetByRole(AriaRole.Button, new() { Name = "Reset password" }).ClickAsync();
        await Expect(device.GetByRole(AriaRole.Heading, new() { Name = "Your password has been reset" })).ToBeVisibleAsync();

        await device.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();
        await SubmitSignInAsync(device, account.Email, account.Password);
        await Expect(device.GetByTestId("login-error")).ToBeVisibleAsync();
        await SubmitSignInAsync(device, account.Email, newPassword);
        // The original sign-in request is preserved through the reset, so the user ends up in the app.
        await ExpectDashboardAsync(device);
    }
}
