using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Starter.Identity.Data;
using Starter.Identity.Infrastructure;
using Starter.Identity.Infrastructure.Email;

namespace Starter.Identity.Pages;

/// <summary>Base class for account pages: validated return URLs and absolute links for emails.</summary>
public abstract class AccountPageModel : PageModel
{
    /// <summary>Where to continue after the page's task is done. Always validated before use.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    protected PublicUrls Urls => HttpContext.RequestServices.GetRequiredService<PublicUrls>();

    /// <summary>The validated return URL, or the default one when it is missing or not allowed.</summary>
    public string SafeReturnUrl => Urls.Normalize(ReturnUrl);

    /// <summary>Redirects only to a local path or a registered client origin (no open redirect).</summary>
    protected RedirectResult RedirectToReturnUrl() => Redirect(SafeReturnUrl);

    /// <summary>Absolute link for emails, based on the configured public URL (not the Host header).</summary>
    protected string EmailLink(string page, object values)
    {
        var path = Url.Page(page, pageHandler: null, values: values)
            ?? throw new InvalidOperationException($"Cannot build a link to {page}.");
        return Urls.Absolute(Request, path);
    }

    /// <summary>Generates an email confirmation token and sends the link. The link keeps the return URL so the user can continue where they started.</summary>
    protected async Task SendConfirmationEmailAsync(UserManager<ApplicationUser> users, IAccountEmailSender sender, ApplicationUser user)
    {
        var code = EncodeToken(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = EmailLink("/Account/ConfirmEmail", new { userId = user.Id, code, returnUrl = ReturnUrl });
        await sender.SendConfirmationLinkAsync(user.Email!, user.DisplayName, link, HttpContext.RequestAborted);
    }

    protected static string EncodeToken(string token) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

    protected static string? DecodeToken(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
