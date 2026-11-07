using System.Diagnostics;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Starter.Identity.Pages;

/// <summary>
/// Friendly error page for unhandled exceptions, status codes and OpenID Connect errors (for example an invalid
/// redirect URI). It never shows exception details; the reference id lets support find the log entry.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public string Title { get; private set; } = "Something went wrong";
    public string Message { get; private set; } = "An unexpected error occurred. Try again in a moment.";
    public string? RequestId { get; private set; }

    public void OnGet(int? statusCode) => Describe(statusCode);

    public void OnPost(int? statusCode) => Describe(statusCode);

    private void Describe(int? statusCode)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var oidcError = HttpContext.GetOpenIddictServerResponse();
        if (oidcError is not null)
        {
            Title = "Sign-in request rejected";
            Message = "The application sent an invalid sign-in request. Go back to the application and try again.";
            return;
        }

        (Title, Message) = statusCode switch
        {
            404 => ("Page not found", "The page you are looking for does not exist."),
            429 => ("Too many attempts", "Wait a minute and try again."),
            _ => (Title, Message),
        };
    }
}
