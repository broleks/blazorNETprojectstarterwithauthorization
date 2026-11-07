using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Starter.Api.Tests.Infrastructure;

namespace Starter.Api.Tests.Identity;

/// <summary>Security properties of the account pages on the identity server.</summary>
public sealed class AccountPagesTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Google_button_is_not_shown_when_google_is_not_configured()
    {
        using var browser = Fixture.Identity.CreateBrowser();

        var page = await browser.GetStringAsync("/Account/Login", Cancellation);

        Assert.Contains("Sign in", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Google", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registration_never_shows_the_confirmation_link_on_the_page()
    {
        using var browser = Fixture.Identity.CreateBrowser();
        var email = $"new-{Guid.NewGuid():N}@example.test";
        var form = await browser.GetStringAsync("/Account/Register", Cancellation);

        using var response = await browser.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "New User",
            ["Input.Email"] = email,
            ["Input.Password"] = "a long enough password",
            ["Input.ConfirmPassword"] = "a long enough password",
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var confirmationPage = await browser.GetStringAsync(response.Headers.Location, Cancellation);

        var sent = Assert.Single(Fixture.Emails.Sent, e => e.To == email);
        Assert.Equal("confirm", sent.Subject);
        var code = QueryHelpers.ParseQuery(new Uri(sent.Link).Query)["code"].ToString();
        Assert.DoesNotContain(code, confirmationPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmEmail", confirmationPage, StringComparison.Ordinal);
        // Links in emails use the configured public URL, never the request's Host header.
        Assert.StartsWith(StarterFixture.IdentityUrl, sent.Link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registering_an_existing_email_looks_the_same_and_notifies_the_owner()
    {
        var existing = await Fixture.CreateUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        var form = await browser.GetStringAsync("/Account/Register", Cancellation);

        using var response = await browser.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Someone",
            ["Input.Email"] = existing.Email,
            ["Input.Password"] = "a long enough password",
            ["Input.ConfirmPassword"] = "a long enough password",
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/RegisterConfirmation", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Fixture.Emails.Sent, e => e.To == existing.Email && e.Subject == "exists");
    }

    [Fact]
    public async Task Wrong_password_shows_a_generic_error()
    {
        var user = await Fixture.CreateUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        var form = await browser.GetStringAsync("/Account/Login", Cancellation);

        using var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email,
            ["Input.Password"] = "wrong password!!",
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The email or password is incorrect.", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_form_without_antiforgery_token_is_rejected()
    {
        var user = await Fixture.CreateUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();

        using var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email,
            ["Input.Password"] = user.Password,
        }), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    public async Task Sign_in_never_redirects_to_a_foreign_site(string returnUrl)
    {
        var user = await Fixture.CreateUserAsync();
        using var browser = Fixture.Identity.CreateBrowser();
        var form = await browser.GetStringAsync("/Account/Login", Cancellation);

        using var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email,
            ["Input.Password"] = user.Password,
            ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://web.test/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Unregistered_redirect_uri_is_refused_by_the_authorization_endpoint()
    {
        using var browser = Fixture.Identity.CreateBrowser();

        using var response = await browser.GetAsync(
            "/connect/authorize?client_id=starter-web&response_type=code&scope=openid&code_challenge=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopq&code_challenge_method=S256&redirect_uri=https%3A%2F%2Fevil.example%2Fcallback",
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Authorization_request_without_pkce_is_refused()
    {
        using var browser = Fixture.Identity.CreateBrowser();

        using var response = await browser.GetAsync(
            $"/connect/authorize?client_id=starter-web&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(StarterFixture.WebRedirectUri)}",
            Cancellation);

        // Rejected before any sign-in happens: no login page, no authorization code.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}

/// <summary>The controllable part of the Google integration (the real Google sign-in is verified manually, see docs/google.md).</summary>
public sealed class GoogleSignInTests(StarterFixture fixture) : ApiTestBase(fixture), IAsyncLifetime
{
    private IdentityFactory google = default!;

    public ValueTask InitializeAsync()
    {
        google = new IdentityFactory(Fixture, new()
        {
            ["Authentication:Google:ClientId"] = "test-client-id.apps.googleusercontent.com",
            ["Authentication:Google:ClientSecret"] = "test-client-secret",
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => google.DisposeAsync();

    [Fact]
    public async Task Google_button_is_shown_when_configured()
    {
        using var browser = google.CreateBrowser();

        var page = await browser.GetStringAsync("/Account/Login", Cancellation);

        Assert.Contains("Continue with Google", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Google_sign_in_redirects_to_google_with_our_callback_and_state()
    {
        using var browser = google.CreateBrowser();
        var location = await StartGoogleSignInAsync(browser);

        Assert.Equal("accounts.google.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("test-client-id.apps.googleusercontent.com", query["client_id"].ToString());
        Assert.Equal("https://identity.test/signin-google", query["redirect_uri"].ToString());
        Assert.False(string.IsNullOrEmpty(query["state"].ToString()));
        Assert.Contains("email", query["scope"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_google_sign_in_returns_to_login_with_a_message()
    {
        using var browser = google.CreateBrowser();
        var location = await StartGoogleSignInAsync(browser);
        var state = QueryHelpers.ParseQuery(location.Query)["state"].ToString();

        // What Google sends back when the user presses "Cancel".
        using var callback = await browser.GetAsync($"/signin-google?error=access_denied&state={Uri.EscapeDataString(state)}", Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("externalError=cancelled", callback.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var page = await browser.GetStringAsync(callback.Headers.Location, Cancellation);
        Assert.Contains("Google sign-in was cancelled", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Google_callback_with_forged_state_fails_safely()
    {
        using var browser = google.CreateBrowser();

        using var callback = await browser.GetAsync("/signin-google?code=fake&state=forged", Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("externalError=failed", callback.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    private static async Task<Uri> StartGoogleSignInAsync(HttpClient browser)
    {
        var form = await browser.GetStringAsync("/Account/Login", Cancellation);
        using var response = await browser.PostAsync("/Account/ExternalLogin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["provider"] = "Google",
            ["returnUrl"] = "/Account/Manage",
            ["__RequestVerificationToken"] = OAuthTestClient.AntiforgeryToken(form),
        }), Cancellation);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }
}
