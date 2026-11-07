using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;

namespace Starter.Identity.Pages.Account;

public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ILogger<LoginModel> logger) : AccountPageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ExternalError { get; set; }

    public IReadOnlyList<AuthenticationScheme> ExternalLogins { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool ShowResendConfirmation { get; private set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        // Remove a half-finished external sign-in so it cannot be picked up later.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await LoadExternalLoginsAsync();

        ErrorMessage = ExternalError switch
        {
            "cancelled" => "Google sign-in was cancelled. You can try again or sign in with your email and password.",
            "failed" => "Google sign-in did not complete. Try again or sign in with your email and password.",
            _ => null,
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadExternalLoginsAsync();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            logger.LogInformation("User signed in with a password");
            return RedirectToReturnUrl();
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        if (result.IsNotAllowed && await IsUnconfirmedWithCorrectPasswordAsync())
        {
            ErrorMessage = "Confirm your email address before signing in. Check your inbox for the confirmation email.";
            ShowResendConfirmation = true;
            return Page();
        }

        // One message for unknown email and wrong password, so the form does not reveal which emails are registered.
        ErrorMessage = "The email or password is incorrect.";
        return Page();
    }

    /// <summary>
    /// SignInManager reports "not allowed" for an unconfirmed email before it checks the password. The password
    /// is checked here (counting failures towards lockout) so the hint is shown only to the account owner.
    /// </summary>
    private async Task<bool> IsUnconfirmedWithCorrectPasswordAsync()
    {
        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        if (await userManager.CheckPasswordAsync(user, Input.Password))
        {
            return true;
        }

        await userManager.AccessFailedAsync(user);
        return false;
    }

    private async Task LoadExternalLoginsAsync() =>
        ExternalLogins = [.. await signInManager.GetExternalAuthenticationSchemesAsync()];
}
