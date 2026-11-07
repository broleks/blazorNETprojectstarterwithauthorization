using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;

namespace Starter.Identity.Pages.Account;

public sealed class ConfirmEmailModel(UserManager<ApplicationUser> userManager) : AccountPageModel
{
    public bool Succeeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        var token = DecodeToken(code);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null || token is null)
        {
            return Page();
        }

        Succeeded = user.EmailConfirmed || (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
        return Page();
    }
}
