using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Contracts;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;
using Starter.Identity.Infrastructure.Accounts;

namespace Starter.Identity.Pages.Admin.Users;

/// <summary>
/// One user, for administrators: lock/unlock, grant/revoke the Admin role, delete. Every action is logged with the
/// administrator's id. Administrators cannot lock, demote or delete themselves here (use "Your account" instead),
/// which also prevents removing the last administrator by accident.
/// </summary>
public sealed class DetailsModel(
    UserManager<ApplicationUser> userManager,
    AccountSecurity accountSecurity,
    AccountDeletionService deletion) : AccountPageModel
{
    public ApplicationUser? Account { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsSelf { get; private set; }
    public IReadOnlyList<string> Logins { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string? ConfirmEmail { get; set; }

    public async Task<IActionResult> OnGetAsync(string id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostLockAsync(string id) => await ActAsync(id, "admin.user_locked", async user =>
    {
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await userManager.UpdateSecurityStampAsync(user);
        await accountSecurity.RevokeAllSessionsAsync(user, HttpContext.RequestAborted);
        return "The account is locked and signed out everywhere.";
    });

    public async Task<IActionResult> OnPostUnlockAsync(string id) => await ActAsync(id, "admin.user_unlocked", async user =>
    {
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return "The account is unlocked.";
    });

    public async Task<IActionResult> OnPostGrantAdminAsync(string id) => await ActAsync(id, "admin.role_granted", async user =>
    {
        await userManager.AddToRoleAsync(user, ApiConstants.AdminRole);
        await EndSessionsAsync(user);
        return "The user is now an administrator (after signing in again).";
    });

    public async Task<IActionResult> OnPostRevokeAdminAsync(string id) => await ActAsync(id, "admin.role_revoked", async user =>
    {
        await userManager.RemoveFromRoleAsync(user, ApiConstants.AdminRole);
        await EndSessionsAsync(user);
        return "The Admin role was removed.";
    });

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (IsSelf)
        {
            ErrorMessage = "Delete your own account on your account page.";
            return Page();
        }

        if (!string.Equals(ConfirmEmail?.Trim(), Account!.Email, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Type the user's email address to confirm the deletion.";
            return Page();
        }

        var result = await deletion.DeleteAsync(Account, initiatedBy: userManager.GetUserId(User)!, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ErrorMessage = "The account could not be deleted.";
            return Page();
        }

        TempData["StatusMessage"] = "The account and its data were deleted.";
        return RedirectToPage("./Index");
    }

    private async Task EndSessionsAsync(ApplicationUser user)
    {
        // Roles travel in tokens: a new security stamp and revoked refresh tokens make the user sign in again,
        // so the new role set applies within minutes.
        await userManager.UpdateSecurityStampAsync(user);
        await accountSecurity.RevokeAllSessionsAsync(user, HttpContext.RequestAborted);
    }

    private async Task<IActionResult> ActAsync(string id, string action, Func<ApplicationUser, Task<string>> change)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (IsSelf)
        {
            ErrorMessage = "You cannot change your own account here.";
            return Page();
        }

        StatusMessage = await change(Account!);
        LogSecurity(action, EventOutcome.Success, Account!.Id,
            new Dictionary<string, object?> { ["AdminId"] = userManager.GetUserId(User) });
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(string id)
    {
        Account = await userManager.FindByIdAsync(id);
        if (Account is null)
        {
            return false;
        }

        IsAdmin = await userManager.IsInRoleAsync(Account, ApiConstants.AdminRole);
        IsLocked = Account.LockoutEnd > DateTimeOffset.UtcNow;
        IsSelf = Account.Id == userManager.GetUserId(User);
        Logins = [.. (await userManager.GetLoginsAsync(Account)).Select(l => l.ProviderDisplayName ?? l.LoginProvider)];
        return true;
    }
}
