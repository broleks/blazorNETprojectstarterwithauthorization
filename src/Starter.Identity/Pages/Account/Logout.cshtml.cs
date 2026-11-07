using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Starter.Identity.Pages.Account;

/// <summary>Confirmation shown for sign-out requests that did not come with a valid id_token_hint (possible logout CSRF).</summary>
public sealed class LogoutModel : PageModel
{
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; private set; } = [];

    public void OnGet() =>
        Parameters = [.. Request.Query
            .Where(p => p.Key is "post_logout_redirect_uri" or "state" or "client_id")
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value.ToString()))];
}
