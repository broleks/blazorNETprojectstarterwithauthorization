using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure.Accounts;

namespace Starter.Identity.Pages.Account.Manage;

/// <summary>Download of all personal data (GDPR access right) and the way to account deletion.</summary>
public sealed class PersonalDataModel(UserManager<ApplicationUser> userManager, PersonalDataExporter exporter) : ManagePageModel
{
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostDownloadAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        try
        {
            var data = await exporter.ExportAsync(user, HttpContext.RequestAborted);
            LogSecurity("account.export", EventOutcome.Success, user.Id);
            return File(data, "application/json", $"starter-personal-data-{DateTime.UtcNow:yyyyMMdd}.json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            LogSecurity("account.export", EventOutcome.Failure, user.Id, new Dictionary<string, object?> { ["Reason"] = ex.GetType().Name });
            ErrorMessage = "Your data could not be collected right now. Please try again in a few minutes.";
            return Page();
        }
    }
}
