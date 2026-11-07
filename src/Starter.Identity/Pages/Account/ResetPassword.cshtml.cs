using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;

namespace Starter.Identity.Pages.Account;

public sealed class ResetPasswordModel(UserManager<ApplicationUser> userManager, AccountSecurity accountSecurity) : AccountPageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a new password.")]
        [StringLength(128, MinimumLength = 12, ErrorMessage = "The password must be at least {2} characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm your new password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            ErrorMessage = "The password reset link is invalid. Request a new one.";
            return Page();
        }

        Input.Code = code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        var token = DecodeToken(Input.Code);
        if (user is null || token is null)
        {
            ErrorMessage = "The password reset link is invalid or has expired. Request a new one.";
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            var invalidToken = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken));
            if (invalidToken)
            {
                ErrorMessage = "The password reset link is invalid or has expired. Request a new one.";
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        // ResetPasswordAsync changed the security stamp; also revoke refresh tokens on every device.
        await accountSecurity.RevokeAllSessionsAsync(user, HttpContext.RequestAborted);
        return RedirectToPage("./ResetPasswordConfirmation", new { returnUrl = ReturnUrl });
    }
}
