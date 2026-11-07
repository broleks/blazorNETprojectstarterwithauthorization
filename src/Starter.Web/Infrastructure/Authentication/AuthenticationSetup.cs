using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Starter.Contracts;
using Starter.Web.Infrastructure.Sessions;

namespace Starter.Web.Infrastructure.Authentication;

/// <summary>
/// Starter.Web is a confidential OpenID Connect client of Starter.Identity (Authorization Code flow with PKCE).
/// The browser gets an HttpOnly session cookie; the tokens stay in the server-side session store.
/// </summary>
internal static class AuthenticationSetup
{
    public const string PromptItem = "starter_prompt";
    public const string IdTokenHintItem = "starter_id_token_hint";

    public static void AddWebAuthentication(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Authentication");
        var authority = section["Authority"] ?? throw new InvalidOperationException("'Authentication:Authority' (the Starter.Identity URL) is not configured.");
        var clientId = section["ClientId"] ?? "starter-web";
        var clientSecret = section["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("'Authentication:ClientSecret' is not configured. See docs/configuration.md.");
        }

        builder.Services.AddDataProtection()
            .SetApplicationName("Starter.Web")
            .PersistKeysToDbContext<WebDbContext>();

        builder.Services.AddSingleton<SessionProtector>();
        builder.Services.AddSingleton<ServerSideTicketStore>();
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieSessionStore>();
        builder.Services.AddSingleton<OidcTokenClient>();
        builder.Services.AddSingleton<SessionTokenService>();
        builder.Services.AddHostedService<ExpiredSessionCleanup>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-starter-web";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax: sent on top-level navigation back from the identity server, not on cross-site sub-requests.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                // The code comes back in the query string: no auto-submitting form (and no inline script) is needed.
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = true; // moved into the server-side store by ServerSideTicketStore
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.TokenValidationParameters.NameClaimType = "name";

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.Scope.Add("offline_access"); // refresh token
                options.Scope.Add(ApiConstants.Scope);

                options.SignedOutRedirectUri = "/signed-out";

                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (context.Properties.Items.TryGetValue(PromptItem, out var prompt) && prompt is not null)
                    {
                        context.ProtocolMessage.Prompt = prompt;
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    // The id_token lives in the server-side store, not in the cookie, so it is passed explicitly.
                    // With a valid hint the identity server signs the user out without asking again.
                    if (context.Properties.Items.TryGetValue(IdTokenHintItem, out var idToken) && idToken is not null)
                    {
                        context.ProtocolMessage.IdTokenHint = idToken;
                        context.Properties.Items.Remove(IdTokenHintItem);
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnRemoteFailure = context =>
                {
                    // Expired or tampered sign-in attempts, errors returned by the identity server, network problems.
                    // The user sees a friendly page; details stay in the log.
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Starter.Web.Authentication");
                    logger.LogWarning(context.Failure, "Remote sign-in failed");
                    context.Response.Redirect("/authentication/error");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();
    }

    /// <summary>Sign-in, registration and sign-out endpoints used by the UI.</summary>
    public static void MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/authentication").AllowAnonymous();

        group.MapGet("/login", (string? returnUrl) => Challenge(returnUrl, prompt: null));

        // Opens the identity server's registration page (OpenID Connect prompt=create).
        group.MapGet("/register", (string? returnUrl) => Challenge(returnUrl, prompt: "create"));

        // Signs in again silently (the identity server session is still valid) to pick up profile changes.
        group.MapGet("/refresh", (string? returnUrl) => Challenge(returnUrl, prompt: null));

        group.MapPost("/logout", async (HttpContext context, SessionTokenService tokens, IAntiforgery antiforgery) =>
        {
            // Explicit check: the endpoint binds no form fields, so the antiforgery middleware would not
            // validate requests that are not form posts. Without it another site could sign users out.
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            var properties = new AuthenticationProperties { RedirectUri = "/signed-out" };
            if (context.User.FindFirst(ServerSideTicketStore.SessionKeyClaim)?.Value is { } sessionKey)
            {
                properties.Items[IdTokenHintItem] = await tokens.GetIdTokenAsync(sessionKey, context.RequestAborted);
                // Deletes the server-side session and revokes the refresh token right away.
                await tokens.EndSessionAsync(sessionKey, context.RequestAborted);
            }

            return Results.SignOut(properties, [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .DisableAntiforgery(); // validated explicitly above
    }

    private static IResult Challenge(string? returnUrl, string? prompt)
    {
        var properties = new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) };
        if (prompt is not null)
        {
            properties.Items[PromptItem] = prompt;
        }

        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>Only local paths are accepted as return URLs, which prevents open redirects.</summary>
    public static string LocalUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            ? returnUrl
            : "/";
}

/// <summary>
/// Re-checks the session of an open interactive circuit every minute: if it was ended (sign-out in another tab,
/// refresh token revoked after a password change, expiry) the UI switches to signed-out and asks to sign in again.
/// The check also refreshes the access token ahead of time.
/// </summary>
internal sealed class SessionRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, SessionTokenService tokens)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var sessionKey = authenticationState.User.FindFirst(ServerSideTicketStore.SessionKeyClaim)?.Value;
        if (sessionKey is null)
        {
            return false;
        }

        var result = await tokens.GetAccessTokenAsync(sessionKey, cancellationToken);
        // A temporary identity server outage does not sign the user out.
        return result.Status != AccessTokenStatus.SessionEnded;
    }
}
