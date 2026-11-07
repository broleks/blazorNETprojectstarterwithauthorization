using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;

namespace Starter.Identity.Pages.Account.Manage;

/// <summary>
/// Connects or disconnects Google for the signed-in account. This is the only way a Google account gets linked
/// to an existing local account: the user proves ownership by being signed in, and the callback verifies
/// (through the XSRF value stored in the external cookie) that it belongs to the same user who started it.
/// </summary>
public sealed class ExternalLoginsModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ManagePageModel
{
    public IReadOnlyList<UserLoginInfo> CurrentLogins { get; private set; } = [];
    public IReadOnlyList<AuthenticationScheme> OtherLogins { get; private set; } = [];
    public bool CanRemove { get; private set; }
    public bool AnyProviderConfigured { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        if (!CanRemove)
        {
            ErrorMessage = "Add a password before disconnecting your only sign-in method.";
            return Page();
        }

        var result = await userManager.RemoveLoginAsync(user, loginProvider, providerKey);
        if (!result.Succeeded)
        {
            ErrorMessage = "The sign-in method could not be removed.";
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "The sign-in method was removed.";
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostLinkLoginAsync(string provider)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var redirectUrl = Url.Page("./ExternalLogins", pageHandler: "LinkLoginCallback", values: new { returnUrl = ReturnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, userManager.GetUserId(User));
        properties.Items[Infrastructure.ExternalLoginClaims.ReturnUrlItem] = Url.Page("./ExternalLogins", new { returnUrl = ReturnUrl });
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetLinkLoginCallbackAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Passing the user id makes Identity verify that the external sign-in was started by this user.
        var info = await signInManager.GetExternalLoginInfoAsync(await userManager.GetUserIdAsync(user));
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (info is null)
        {
            StatusMessage = null;
            await LoadAsync(user);
            ErrorMessage = "The sign-in method could not be connected. Try again.";
            return Page();
        }

        var result = await userManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            await LoadAsync(user);
            ErrorMessage = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.LoginAlreadyAssociated))
                ? $"This {info.ProviderDisplayName} account is already connected to another account."
                : "The sign-in method could not be connected.";
            return Page();
        }

        StatusMessage = $"{info.ProviderDisplayName} is now connected to your account.";
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        CurrentLogins = [.. await userManager.GetLoginsAsync(user)];
        var schemes = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        AnyProviderConfigured = schemes.Count > 0;
        OtherLogins = [.. schemes.Where(s => CurrentLogins.All(l => l.LoginProvider != s.Name))];
        CanRemove = user.PasswordHash is not null || CurrentLogins.Count > 1;
    }
}
