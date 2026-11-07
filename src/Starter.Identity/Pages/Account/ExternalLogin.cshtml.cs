using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Pages.Account;

/// <summary>
/// Sign-in with Google. Rules that keep accounts safe:
/// <list type="bullet">
/// <item>A Google account signs in only to the local account it was explicitly linked to.</item>
/// <item>A matching email address is never enough to link: the owner must sign in with their password first
/// and link Google from the account page. Otherwise anyone controlling a Google account with that address
/// (e.g. a recycled work address) could take over the local account.</item>
/// <item>A new local account created from Google skips email confirmation only if Google reports the address as verified.</item>
/// </list>
/// </summary>
public sealed class ExternalLoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAccountEmailSender emailSender,
    TimeProvider clock,
    ILogger<ExternalLoginModel> logger) : AccountPageModel
{
    public enum PageState { CreateAccount, AccountExists, EmailMissing }

    public PageState State { get; private set; }
    public string ProviderDisplayName { get; private set; } = "Google";
    public string? Email { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter a display name.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "The display name can be at most {1} characters long.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    public IActionResult OnGet() => RedirectToPage("./Login");

    /// <summary>Starts the external sign-in (POST only, so it cannot be triggered by a link on another site).</summary>
    public async Task<IActionResult> OnPostAsync(string provider)
    {
        var scheme = (await signInManager.GetExternalAuthenticationSchemesAsync()).FirstOrDefault(s => s.Name == provider);
        if (scheme is null)
        {
            return RedirectToPage("./Login", new { returnUrl = ReturnUrl });
        }

        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl = ReturnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(scheme.Name, redirectUrl);
        properties.Items[ExternalLoginClaims.ReturnUrlItem] = SafeReturnUrl;
        return Challenge(properties, scheme.Name);
    }

    public async Task<IActionResult> OnGetCallbackAsync()
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return RedirectToPage("./Login", new { returnUrl = ReturnUrl, externalError = "failed" });
        }

        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            logger.LogInformation("User signed in with {Provider}", info.LoginProvider);
            return RedirectToReturnUrl();
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        if (result.IsNotAllowed)
        {
            // Linked account exists but its email is not confirmed yet.
            return RedirectToPage("./RegisterConfirmation", new { returnUrl = ReturnUrl });
        }

        // No local account is linked to this Google account.
        await DescribeAsync(info);
        DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        return Page();
    }

    /// <summary>Creates a new local account for a Google account that is not linked to anything yet.</summary>
    public async Task<IActionResult> OnPostConfirmationAsync()
    {
        // The email always comes from the external sign-in, never from the form.
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return RedirectToPage("./Login", new { returnUrl = ReturnUrl, externalError = "failed" });
        }

        await DescribeAsync(info);
        if (State != PageState.CreateAccount || !ModelState.IsValid)
        {
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = Email,
            Email = Email,
            DisplayName = DisplayName.Trim(),
            EmailConfirmed = ExternalLoginClaims.IsEmailVerified(info.Principal),
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await userManager.CreateAsync(user);
        var linked = created.Succeeded ? await userManager.AddLoginAsync(user, info) : created;
        if (!linked.Succeeded)
        {
            foreach (var error in linked.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("Account {UserId} created with {Provider}", user.Id, info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (!user.EmailConfirmed)
        {
            await SendConfirmationEmailAsync(userManager, emailSender, user);
            return RedirectToPage("./RegisterConfirmation", new { returnUrl = ReturnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return RedirectToReturnUrl();
    }

    private async Task DescribeAsync(ExternalLoginInfo info)
    {
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        Email = info.Principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrEmpty(Email))
        {
            State = PageState.EmailMissing;
        }
        else if (await userManager.FindByEmailAsync(Email) is not null)
        {
            // Deliberately no automatic linking - see the class comment.
            State = PageState.AccountExists;
        }
        else
        {
            State = PageState.CreateAccount;
        }
    }
}
