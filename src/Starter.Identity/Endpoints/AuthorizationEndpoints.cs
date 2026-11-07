using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Starter.Identity.Endpoints;

/// <summary>
/// The parts of the OpenID Connect protocol that need application logic. OpenIddict validates every request
/// (client, redirect URI, PKCE, scopes) before these handlers run; they only decide who the user is.
/// </summary>
internal static class AuthorizationEndpoints
{
    public static void MapAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], Authorize).AllowAnonymous();
        app.MapPost("/connect/token", Exchange).AllowAnonymous().DisableAntiforgery();
        app.MapGet("/connect/endsession", EndSession).AllowAnonymous();
        app.MapPost("/connect/endsession", ConfirmEndSession).AllowAnonymous();
    }

    private static async Task<IResult> Authorize(
        HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await userManager.GetUserAsync(cookie.Principal) : null;

        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.LoginRequired, "The user is not signed in.");
            }

            // After signing in, the browser comes back to this exact authorization request.
            var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

            // prompt=create (OpenID Connect "Prompt Create") opens the registration page instead of sign-in.
            if (request.HasPromptValue(PromptValues.Create))
            {
                var withoutPrompt = context.Request.PathBase + context.Request.Path + QueryString.Create(
                    context.Request.Query.Where(p => p.Key != Parameters.Prompt));
                return Results.Redirect("/Account/Register" + QueryString.Create("returnUrl", withoutPrompt));
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [IdentityConstants.ApplicationScheme]);
        }

        var principal = AccountSecurity.CreateTokenPrincipal(user, request.GetScopes());
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Exchange(
        HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(Errors.UnsupportedGrantType, "The grant type is not supported.");
        }

        // OpenIddict has already validated the code or refresh token (signature, expiry, revocation, client binding).
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var userId = result.Principal?.GetClaim(Claims.Subject);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);

        // The account may have been deleted, or may no longer be allowed to sign in.
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(Errors.InvalidGrant, "The account is no longer allowed to sign in.");
        }

        // A password change or reset updates the security stamp; older refresh tokens stop working.
        if (!string.Equals(result.Principal!.GetClaim(AccountSecurity.SecurityStampClaim), user.SecurityStamp, StringComparison.Ordinal))
        {
            return Forbid(Errors.InvalidGrant, "The session is no longer valid.");
        }

        // Claims are rebuilt from the database so a changed display name reaches the next access token.
        var principal = AccountSecurity.CreateTokenPrincipal(user, result.Principal!.GetScopes());
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// RP-initiated logout. When the client sent a valid <c>id_token_hint</c> (Starter.Web always does) the user is
    /// signed out immediately. Without it the request could come from any site, so the user is asked to confirm.
    /// </summary>
    private static async Task<IResult> EndSession(HttpContext context, SignInManager<ApplicationUser> signInManager)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is null)
        {
            return Results.Redirect("/Account/Logout" + context.Request.QueryString);
        }

        return await SignOutAsync(signInManager);
    }

    private static async Task<IResult> ConfirmEndSession(HttpContext context, IAntiforgery antiforgery, SignInManager<ApplicationUser> signInManager)
    {
        await antiforgery.ValidateRequestAsync(context);
        return await SignOutAsync(signInManager);
    }

    private static async Task<IResult> SignOutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();

        // OpenIddict redirects to the client's validated post_logout_redirect_uri, or to RedirectUri otherwise.
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/Account/LoggedOut" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IResult Forbid(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
