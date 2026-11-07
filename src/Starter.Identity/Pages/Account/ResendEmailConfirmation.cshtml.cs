using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Pages.Account;

public sealed class ResendEmailConfirmationModel(UserManager<ApplicationUser> userManager, IAccountEmailSender emailSender) : AccountPageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    public bool Sent { get; private set; }

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
        if (user is { EmailConfirmed: false })
        {
            await SendConfirmationEmailAsync(userManager, emailSender, user);
        }

        // Same answer whether or not the account exists.
        Sent = true;
        return Page();
    }
}
