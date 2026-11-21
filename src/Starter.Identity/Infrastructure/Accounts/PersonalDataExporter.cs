using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Starter.Identity.Data;

namespace Starter.Identity.Infrastructure.Accounts;

/// <summary>
/// Builds the user's data export (GDPR "right of access"): the account kept by the identity server plus the
/// data of every API feature, as one JSON document.
/// </summary>
public sealed class PersonalDataExporter(UserManager<ApplicationUser> users, ApiServiceClient api, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<byte[]> ExportAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var logins = await users.GetLoginsAsync(user);
        var export = new
        {
            ExportedAt = clock.GetUtcNow(),
            Account = new
            {
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.DisplayName,
                user.CreatedAt,
                HasPassword = user.PasswordHash is not null,
                ExternalLogins = logins.Select(l => l.ProviderDisplayName ?? l.LoginProvider),
                Roles = await users.GetRolesAsync(user),
            },
            ApplicationData = await api.ExportUserDataAsync(user.Id, cancellationToken),
        };

        return JsonSerializer.SerializeToUtf8Bytes(export, Options);
    }
}
