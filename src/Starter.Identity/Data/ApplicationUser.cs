using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Starter.Identity.Data;

/// <summary>An account managed by this application. Passwords, lockout and security stamps come from ASP.NET Core Identity.</summary>
public sealed class ApplicationUser : IdentityUser
{
    public const int DisplayNameMaxLength = 100;

    /// <summary>Name shown in the UI. Independent of the email address and not unique.</summary>
    [MaxLength(DisplayNameMaxLength)]
    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
