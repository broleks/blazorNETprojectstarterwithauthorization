using System.Net;
using System.Net.Http.Json;
using Starter.Api.Tests.Infrastructure;

namespace Starter.Api.Tests.Security;

/// <summary>
/// The API's real authentication setup (OpenIddict validation, keys from the identity server's discovery
/// document) must reject every token that is not a valid, unexpired access token for this API with the API scope.
/// </summary>
public sealed class AccessTokenValidationTests(StarterFixture fixture) : ApiTestBase(fixture)
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/me" },
        { "GET", "/api/dashboard" },
        { "GET", "/api/notes" },
        { "GET", $"/api/notes/{Guid.NewGuid()}" },
        { "POST", "/api/notes" },
        { "PUT", $"/api/notes/{Guid.NewGuid()}" },
        { "DELETE", $"/api/notes/{Guid.NewGuid()}" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Request_without_token_is_rejected_with_401(string method, string path)
    {
        using var response = await SendAsync(new HttpMethod(method), path, accessToken: null, body: method is "POST" or "PUT" ? new { title = "x" } : null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_are_public()
    {
        using var response = await GetAsync("/health/live", accessToken: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_issued_by_the_identity_server_is_accepted_and_identifies_the_user()
    {
        var (user, tokens) = await SignedInUserAsync("Ada");

        using var response = await GetAsync("/api/me", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<Starter.Contracts.Account.MeResponse>(Cancellation);
        Assert.Equal(user.Id, me!.UserId);
        Assert.Equal("Ada", me.DisplayName);
        Assert.Equal(user.Email, me.Email);
        Assert.Contains("starter_api", me.Scopes);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.")]
    public async Task Malformed_or_unsigned_token_is_rejected(string token)
    {
        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_modified_payload_is_rejected()
    {
        var (_, tokens) = await SignedInUserAsync();
        var parts = tokens.AccessToken.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(parts[1]));
        var forged = payload.Replace("\"sub\":\"", "\"sub\":\"x", StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(forged))}.{parts[2]}";

        using var response = await GetAsync("/api/me", tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Correctly_minted_token_is_accepted_positive_control()
    {
        var token = TestTokens.Create(Fixture.SigningCertificate, subject: Guid.NewGuid().ToString());

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = TestTokens.Create(Fixture.SigningCertificate, subject: Guid.NewGuid().ToString(),
            notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1));

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_for_another_api_is_rejected()
    {
        var token = TestTokens.Create(Fixture.SigningCertificate, subject: Guid.NewGuid().ToString(), audience: "another-api");

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_from_another_issuer_is_rejected()
    {
        var token = TestTokens.Create(Fixture.SigningCertificate, subject: Guid.NewGuid().ToString(), issuer: "https://evil.example/");

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_unknown_key_is_rejected()
    {
        using var foreign = TestTokens.ForeignCertificate();
        var token = TestTokens.Create(foreign, subject: Guid.NewGuid().ToString());

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_without_api_scope_is_forbidden()
    {
        var token = TestTokens.Create(Fixture.SigningCertificate, subject: Guid.NewGuid().ToString(), scope: "openid profile");

        using var response = await GetAsync("/api/me", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Real_token_requested_without_api_scope_is_rejected()
    {
        // Without the API scope the identity server does not add the API audience, so the token is not for this API.
        var user = await Fixture.CreateUserAsync();
        var tokens = await OAuthTestClient.SignInAsync(Fixture.Identity, user, scopes: "openid profile email offline_access");

        using var response = await GetAsync("/api/me", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Identity_token_cannot_be_used_as_access_token()
    {
        // Also covers "a sign-in token from another provider (e.g. Google) is not an API token":
        // only access tokens (typ at+jwt) issued for this API by this issuer are accepted.
        var (_, tokens) = await SignedInUserAsync();
        Assert.NotNull(tokens.IdToken);

        using var response = await GetAsync("/api/me", tokens.IdToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
