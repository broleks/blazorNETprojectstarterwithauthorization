using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Starter.Identity.Pages;

/// <summary>The identity server has no home page of its own: send people to their account (sign-in if needed).</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Manage/Index");
}
