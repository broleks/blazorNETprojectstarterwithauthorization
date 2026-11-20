using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Pages.Account;

public sealed class RegisterModel(
    UserManager<ApplicationUser> userManager,
    IAccountEmailSender emailSender,
    TimeProvider clock,
    ILogger<RegisterModel> logger) : AccountPageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter a display name.")]
        [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "The display name can be at most {1} characters long.")]
        [Display(Name = "Display name")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a password.")]
        [StringLength(128, MinimumLength = ApplicationUser.PasswordMinLength, ErrorMessage = "The password must be at least {2} characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existing = await userManager.FindByEmailAsync(Input.Email);
        if (existing is not null)
        {
            // The page never reveals that the email is registered; the owner learns it by email instead.
            if (existing.EmailConfirmed)
            {
                await emailSender.SendAccountExistsNoticeAsync(existing.Email!, existing.DisplayName,
                    EmailLink("/Account/Login", new { returnUrl = ReturnUrl }), HttpContext.RequestAborted);
            }
            else
            {
                // Registered earlier but never confirmed (e.g. the first email was lost): send a fresh confirmation
                // link. The existing account and its password are not changed - only the inbox owner can confirm it.
                await SendConfirmationEmailAsync(userManager, emailSender, existing);
            }

            return RedirectToPage("./RegisterConfirmation", new { returnUrl = ReturnUrl });
        }

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            DisplayName = Input.DisplayName.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };

        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("New account created: {UserId}", user.Id);
        await SendConfirmationEmailAsync(userManager, emailSender, user);
        return RedirectToPage("./RegisterConfirmation", new { returnUrl = ReturnUrl });
    }
}
