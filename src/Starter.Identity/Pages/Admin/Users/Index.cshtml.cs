using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Starter.Contracts;
using Starter.Identity.Data;

namespace Starter.Identity.Pages.Admin.Users;

/// <summary>User list for administrators (folder /Admin requires the Admin role, see Program.cs).</summary>
public sealed class IndexModel(UserManager<ApplicationUser> userManager) : AccountPageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    [TempData]
    public string? StatusMessage { get; set; }

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public int TotalCount { get; private set; }
    public bool HasNextPage => PageNumber * PageSize < TotalCount;

    public sealed record UserRow(string Id, string DisplayName, string? Email, bool EmailConfirmed, DateTimeOffset CreatedAt, bool IsLocked, bool IsAdmin);

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(1, PageNumber);
        var query = userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(u => u.Email!.Contains(term) || u.DisplayName.Contains(term));
        }

        TotalCount = await query.CountAsync();
        var page = await query.OrderBy(u => u.Email)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize)
            .ToListAsync();

        var admins = (await userManager.GetUsersInRoleAsync(ApiConstants.AdminRole)).Select(u => u.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        Users = [.. page.Select(u => new UserRow(u.Id, u.DisplayName, u.Email, u.EmailConfirmed, u.CreatedAt,
            u.LockoutEnd > now, admins.Contains(u.Id)))];
    }
}
