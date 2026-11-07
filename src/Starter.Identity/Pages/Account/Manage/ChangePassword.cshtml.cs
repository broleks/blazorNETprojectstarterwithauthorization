using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;

namespace Starter.Identity.Pages.Account.Manage;

public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountSecurity accountSecurity) : ManagePageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter your current password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string OldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a new password.")]
        [StringLength(128, MinimumLength = 12, ErrorMessage = "The password must be at least {2} characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm your new password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        return await userManager.HasPasswordAsync(user) ? Page() : RedirectToPage("./SetPassword", new { returnUrl = ReturnUrl });
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

        var result = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        // The security stamp changed: other browsers lose their identity session within a minute and their
        // refresh tokens are revoked now. This browser keeps its session (RefreshSignInAsync).
        await accountSecurity.RevokeAllSessionsAsync(user, HttpContext.RequestAborted);
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Your password has been changed. You have been signed out on your other devices. Return to the app to continue.";
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }
}
