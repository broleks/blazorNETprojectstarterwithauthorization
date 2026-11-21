using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Accounts;

namespace Starter.Identity.Pages.Account.Manage;

/// <summary>
/// Immediate account deletion by the user. Requires the password (or, for accounts without a password, typing the
/// email address) so that an unattended signed-in browser is not enough.
/// </summary>
public sealed class DeleteAccountModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountDeletionService deletion) : ManagePageModel
{
    public bool RequirePassword { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [BindProperty]
    [Display(Name = "Your email address")]
    public string? ConfirmEmail { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        RequirePassword = await userManager.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        RequirePassword = await userManager.HasPasswordAsync(user);
        var confirmed = RequirePassword
            ? !string.IsNullOrEmpty(Password) && await userManager.CheckPasswordAsync(user, Password)
            : string.Equals(ConfirmEmail?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase);
        if (!confirmed)
        {
            LogSecurity("account.delete", EventOutcome.Failure, user.Id, new Dictionary<string, object?> { ["Reason"] = "confirmation_failed" });
            ErrorMessage = RequirePassword ? "The password is incorrect." : "The email address does not match your account.";
            return Page();
        }

        var result = await deletion.DeleteAsync(user, initiatedBy: "self", HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ErrorMessage = "Your account could not be deleted. Please try again.";
            return Page();
        }

        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/AccountDeleted", new { returnUrl = ReturnUrl });
    }
}
