using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Starter.Api.Tests.Infrastructure;

public sealed record TokenSet(string AccessToken, string? RefreshToken, string? IdToken);

/// <summary>
/// Plays the role of Starter.Web against the in-memory identity server: Authorization Code flow with PKCE,
/// sign-in through the real login form (with its antiforgery token), code exchange, refresh and revocation.
/// </summary>
public static partial class OAuthTestClient
{
    public const string DefaultScopes = "openid profile email offline_access starter_api";

    public static async Task<TokenSet> SignInAsync(IdentityFactory identity, TestUser user, string scopes = DefaultScopes)
    {
        using var browser = identity.CreateBrowser();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Guid.NewGuid().ToString("N");

        var authorize = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = StarterFixture.ClientId,
            ["redirect_uri"] = StarterFixture.WebRedirectUri,
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        // 1. Not signed in yet: the identity server sends the browser to its login page.
        var response = await browser.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var loginUrl = new Uri(new Uri(StarterFixture.IdentityUrl), response.Headers.Location!);
        Assert.Equal("/Account/Login", loginUrl.AbsolutePath);

        // 2. Submit the real login form.
        var loginPage = await browser.GetStringAsync(loginUrl);
        var returnUrl = QueryHelpers.ParseQuery(loginUrl.Query)["ReturnUrl"].ToString();
        response = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email,
            ["Input.Password"] = user.Password,
            ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = AntiforgeryToken(loginPage),
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // 3. Back at the authorization endpoint, now signed in: redirect to the client with a code.
        response = await browser.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var callback = response.Headers.Location!;
        Assert.StartsWith(StarterFixture.WebRedirectUri, callback.ToString(), StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal(state, query["state"].ToString());

        // 4. Exchange the code (server to server, with the client secret and the PKCE verifier).
        return await RequestTokensAsync(identity, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = query["code"].ToString(),
            ["redirect_uri"] = StarterFixture.WebRedirectUri,
            ["code_verifier"] = verifier,
        });
    }

    public static Task<TokenSet> RefreshAsync(IdentityFactory identity, string refreshToken) =>
        RequestTokensAsync(identity, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

    /// <summary>Returns the HTTP status and OAuth error of a refresh attempt that is expected to fail.</summary>
    public static async Task<(HttpStatusCode Status, string? Error)> TryRefreshAsync(IdentityFactory identity, string refreshToken)
    {
        using var client = identity.CreateClient();
        using var response = await client.PostAsync("/connect/token", ClientForm(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null);
    }

    public static async Task RevokeAsync(IdentityFactory identity, string token)
    {
        using var client = identity.CreateClient();
        using var response = await client.PostAsync("/connect/revoke", ClientForm(new()
        {
            ["token"] = token,
            ["token_type_hint"] = "refresh_token",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public static string AntiforgeryToken(string html) =>
        AntiforgeryRegex().Match(html) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("No antiforgery token found in the page.");

    private static async Task<TokenSet> RequestTokensAsync(IdentityFactory identity, Dictionary<string, string> parameters)
    {
        using var client = identity.CreateClient();
        using var response = await client.PostAsync("/connect/token", ClientForm(parameters));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Token request failed: {body}");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new TokenSet(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("id_token", out var id) ? id.GetString() : null);
    }

    private static FormUrlEncodedContent ClientForm(Dictionary<string, string> parameters)
    {
        parameters["client_id"] = StarterFixture.ClientId;
        parameters["client_secret"] = StarterFixture.ClientSecret;
        return new FormUrlEncodedContent(parameters);
    }

    private static string Base64Url(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();
}
