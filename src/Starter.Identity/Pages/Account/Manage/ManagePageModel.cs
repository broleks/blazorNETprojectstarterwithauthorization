using Microsoft.AspNetCore.Mvc;

namespace Starter.Identity.Pages.Account.Manage;

/// <summary>Base class for "Your account" pages. Requires sign-in (see the folder convention in Program.cs).</summary>
public abstract class ManagePageModel : AccountPageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Link back to the application the user came from (validated), or the configured app URL.</summary>
    public string? ReturnToAppUrl => Urls.IsAllowed(ReturnUrl) ? ReturnUrl : Urls.AppUrl;
}
