using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;

namespace Starter.Identity.Pages.Account.Manage;

/// <summary>Lets an account created with Google add a password, so it does not depend on one provider.</summary>
public sealed class SetPasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountSecurity accountSecurity) : ManagePageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "The password must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Confirm your new password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        return await userManager.HasPasswordAsync(user) ? RedirectToPage("./ChangePassword", new { returnUrl = ReturnUrl }) : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await userManager.AddPasswordAsync(user, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await accountSecurity.RevokeAllSessionsAsync(user, HttpContext.RequestAborted);
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Your password has been set.";
        return RedirectToPage("./ChangePassword", new { returnUrl = ReturnUrl });
    }
}
