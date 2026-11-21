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
            LogSecurity("account.confirm_email", EventOutcome.Failure, user?.Id, new Dictionary<string, object?> { ["Reason"] = "invalid_link" });
            return Page();
        }

        var alreadyConfirmed = user.EmailConfirmed;
        Succeeded = alreadyConfirmed || (await userManager.ConfirmEmailAsync(user, token)).Succeeded;
        LogSecurity("account.confirm_email", Succeeded ? EventOutcome.Success : EventOutcome.Failure, user.Id,
            new Dictionary<string, object?> { ["Reason"] = alreadyConfirmed ? "already_confirmed" : Succeeded ? null : "invalid_or_expired_token" });
        return Page();
    }
}
