using System.Security.Claims;
using OpenIddict.Abstractions;
using Starter.Api.Infrastructure;
using Starter.Contracts.Account;

namespace Starter.Api.Features.Account;

internal static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMe).WithTags("Account");
        return api;
    }

    /// <summary>Returns who the API believes the caller is. Everything comes from the validated access token.</summary>
    private static MeResponse GetMe(ClaimsPrincipal user) => new(
        user.GetUserId(),
        user.FindFirstValue(OpenIddictConstants.Claims.Name) ?? string.Empty,
        user.FindFirstValue(OpenIddictConstants.Claims.Email),
        user.GetScopes().Order(StringComparer.Ordinal).ToList());
}
