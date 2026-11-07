namespace Starter.Identity.Pages.Account;

/// <summary>Never shows the confirmation link itself: it is delivered only by email.</summary>
public sealed class RegisterConfirmationModel : AccountPageModel
{
    public void OnGet()
    {
    }
}
