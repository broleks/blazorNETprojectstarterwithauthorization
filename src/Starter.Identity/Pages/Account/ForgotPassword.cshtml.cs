using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Pages.Account;

public sealed class ForgotPasswordModel(UserManager<ApplicationUser> userManager, IAccountEmailSender emailSender) : AccountPageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Email);
        if (user is { EmailConfirmed: true })
        {
            var code = EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user));
            var link = EmailLink("/Account/ResetPassword", new { code, returnUrl = ReturnUrl });
            await emailSender.SendPasswordResetLinkAsync(user.Email!, user.DisplayName, link, HttpContext.RequestAborted);
        }

        // Same answer whether or not the account exists, so the form cannot be used to find registered emails.
        return RedirectToPage("./ForgotPasswordConfirmation", new { returnUrl = ReturnUrl });
    }
}
