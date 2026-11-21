# Account deletion, data export and administrators

## Data export (GDPR right of access)

*Your account → Your data → Download my data* (identity server; linked from the web app's Account page).

The identity server collects the account (email, display name, creation date, sign-in methods, roles) and asks
the API for the user's data from every feature, then returns one JSON file. The API part comes from the
`IUserDataHandler` implementations (see below). Password hashes, security stamps and tokens are never exported.

## Account deletion (GDPR right to erasure)

*Your account → Your data → Delete my account*, confirmed with the password (accounts without a password type their
email address). Administrators can delete other accounts on the user's detail page. Deletion is immediate:

1. All refresh tokens of the user are revoked.
2. In one database transaction the account is deleted and a row is added to `identity.PendingDataDeletions`.
3. OpenIddict token and authorization entries of the user are removed; a confirmation email is sent; the event is
   logged (`account.delete`, with `InitiatedBy`).
4. `PendingDeletionProcessor` (background service in Starter.Identity) calls the API
   (`DELETE /api/internal/users/{id}`) and retries every minute until it succeeds.
5. An access token issued before the deletion stays valid for up to 5 minutes, so an open browser tab could still
   store data in that window. The processor therefore repeats the API deletion until the token lifetime (+1 minute)
   has passed, and only then removes the pending row.

What remains afterwards, by design:

| Where | What | Until |
| --- | --- | --- |
| Logs (files, Elasticsearch) | events with the `user.id` (no email, no content) | retention, 7 days by default |
| Database backups | everything, as at backup time | backup retention of your database (Azure SQL: point-in-time restore window) |
| Web sessions (`web.Sessions`) | the session row of an open browser | its expiry; it cannot get tokens any more |

Mention these in your privacy policy.

### Service-to-service channel

The identity server calls the API as its own **service client** `starter-identity-service`: OAuth client credentials,
scope `starter_api.accounts`, no user. The API's internal endpoints (`/api/internal/users/{id}/export`,
`DELETE /api/internal/users/{id}`) accept only that scope — a user's token, even an administrator's, gets 403 — and
the service token cannot call the normal API. The client secret is in `OpenIddict:Clients:starter-identity-service:ClientSecret`
(`SERVICE_CLIENT_SECRET` in `.env`, Key Vault secret `service-client-secret` in Azure).

### Every feature with user data must take part

Implement `IUserDataHandler` in the feature folder and register it in `src/Starter.Api/Program.cs`:

```csharp
internal sealed class TasksUserData(AppDbContext db) : IUserDataHandler
{
    public string Name => "tasks";
    public IReadOnlyCollection<Type> Entities => [typeof(TaskItem)];

    public async Task<object> ExportAsync(string userId, CancellationToken ct) =>
        await db.Tasks.AsNoTracking().Where(t => t.OwnerId == userId)
            .Select(t => new { t.Title, t.DueDate, t.CreatedAt }).ToListAsync(ct);

    public Task DeleteAsync(string userId, CancellationToken ct) =>
        db.Tasks.Where(t => t.OwnerId == userId).ExecuteDeleteAsync(ct);
}
```

```csharp
builder.Services.AddScoped<IUserDataHandler, TasksUserData>();
```

The test `Every_entity_with_an_owner_has_a_gdpr_handler` fails if an entity with an `OwnerId` property is not
listed in any handler's `Entities`.

## Administrators

- Role `Admin` (ASP.NET Core Identity). It is put into the access token (for the API) and the identity token (for the
  web app's menu).
- **API:** `AuthenticationSetup.AdminPolicy` = the normal API policy + role Admin. Example:
  `GET /api/admin/stats` (totals only — administrators do not read other users' content).
- **Web:** page `/admin` (`[Authorize(Roles = ApiConstants.AdminRole)]`) and an *Admin* menu item that only
  administrators see. Hiding the menu is convenience; the API enforces the role on every call.
- **Identity:** `/Admin/Users` — search, details, lock/unlock, make administrator / remove role, delete account.
  Administrators cannot change or delete their own account there. Every action is logged with the administrator's id
  (`admin.user_locked`, `admin.role_granted`, … in the Security category).
- **Role changes take effect at the next sign-in.** Granting or removing the role, and locking, update the security
  stamp and revoke the user's refresh tokens, so the user signs in again within minutes and gets the new role set.

### The first administrator

Set the email of an **existing account with a confirmed email**:

| Where | Setting |
| --- | --- |
| Docker Compose | `.env`: `INITIAL_ADMIN_EMAIL=you@example.com` |
| `dotnet run` / Visual Studio | `INITIAL_ADMIN_EMAIL` in `.env`, then `dotnet run scripts/setup-local.cs` (writes the user secret `Admin:InitialAdmins:0`) |
| Azure | Bicep parameter `initialAdminEmail` (app setting `Admin__InitialAdmins__0`) |

The role is given by the identity server's migration step (`--migrate`, or Development startup), so restart the
identity server (Compose: `docker compose --profile app up -d identity-migrate identity`). The list never removes
anyone; further administrators are appointed on `/Admin/Users`. Then sign out and in again to get the role in your tokens.

### Adding an admin feature

- API: put endpoints in a group with `.RequireAuthorization(AuthenticationSetup.AdminPolicy)` (see
  `Features/Admin/AdminEndpoints.cs`). Return what an administrator needs, not more.
- Web: a page with `@attribute [Authorize(Roles = ApiConstants.AdminRole)]` and a typed client like `AdminApi`.
- Add an integration test that a normal user gets 403.
