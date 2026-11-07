using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;

namespace Starter.Identity.Pages.Account.Manage;

public sealed class IndexModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : ManagePageModel
{
    public string Email { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter a display name.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "The display name can be at most {1} characters long.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Load(user);
        DisplayName = user.DisplayName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Load(user);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = DisplayName.Trim();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Your profile could not be saved. Try again.");
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Your profile has been updated. Return to the app to see the change.";
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }

    private void Load(ApplicationUser user)
    {
        Email = user.Email ?? string.Empty;
        CreatedAt = user.CreatedAt;
    }
}
