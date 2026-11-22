# Configuration

Settings come from `appsettings.json`, `appsettings.{Environment}.json`, user secrets (Development only),
environment variables (`Section__Key`) and, in Azure, App Service settings with Key Vault references.
**Secrets are never stored in `appsettings*.json`.**

## Startup check

Each application checks its configuration before it starts and stops with one message listing **every** problem:

```
Starter.Api cannot start - 2 configuration problems:
 - ConnectionStrings:Default is missing (SQL Server connection for the app schema).
 - Authentication:Issuer must be an absolute https URL.
Locally run 'dotnet run scripts/setup-local.cs'. All settings: docs/configuration.md.
```

Messages name the setting, never its value. The requirements of each application are in one file:
`src/Starter.<App>/Infrastructure/ConfigurationRequirements.cs` (helpers: `src/Starter.Observability/ConfigurationCheck.cs`).
When you add a setting your code cannot work without, add a line there.

What is checked: connection strings, URLs (absolute, https), client registrations (redirect URIs, secrets of the web
and service clients), token certificates (file exists and opens with the password; required outside Development),
SMTP settings (unencrypted SMTP refused in Production), Google (both values or none), initial administrators
(email format), CORS origins, logging destination (Elasticsearch URL when used), numbers and durations.

## Starter.Identity

| Key | Example | Secret | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings:Default` | `Server=...;User Id=starter_identity;...` | yes | Runtime user (schema `identity`) |
| `ConnectionStrings:Migrations` | migrator user | yes | Only for `--migrate` / Development startup |
| `Authentication:Issuer` | `https://id.example.com/` | | Public URL; required outside Development; base of email links |
| `Authentication:DefaultReturnUrl` | `https://app.example.com/` | | Where users go after signing in directly on the identity server |
| `Authentication:Google:ClientId` / `ClientSecret` | | yes | Leave empty to disable Google |
| `OpenIddict:AccessTokenLifetime` | `00:05:00` | | |
| `OpenIddict:RefreshTokenLifetime` | `08:00:00` | | |
| `OpenIddict:SigningCertificate:Path` / `Password` | `/var/ssl/private/<thumb>.p12` | yes | Required outside Development |
| `OpenIddict:EncryptionCertificate:Path` / `Password` | | yes | Required outside Development |
| `OpenIddict:Clients:<clientId>:ClientSecret` | | yes | Empty = public client (mobile) |
| `OpenIddict:Clients:<clientId>:RedirectUris:N` | `https://app.example.com/signin-oidc` | | Exact match |
| `OpenIddict:Clients:<clientId>:PostLogoutRedirectUris:N` | `https://app.example.com/signout-callback-oidc` | | |
| `OpenIddict:Clients:starter-identity-service:ClientSecret` | | yes | Service client used for account deletion/export (`Kind: Service`) |
| `Api:BaseUrl` | `https://api.example.com/` | | The API, for account deletion and data export |
| `Admin:InitialAdmins:N` | `you@example.com` | | Existing, confirmed accounts promoted to Admin by the migration step |
| `Email:FromAddress`, `FromName`, `Host`, `Port` | | | SMTP relay |
| `Email:Security` | `StartTls` | | `None` only for local Mailpit |
| `Email:UserName` / `Password` | | yes | |
| `Email:MaxPerRecipientPerHour` | `5` | | |
| `Email:LocalInboxUrl` | `http://localhost:8025` | | Local only: account pages show a link to the test inbox. Leave empty in production |
| `RateLimiting:AccountPostsPerMinute` | `20` | | Per client IP |
| `RateLimiting:TokenRequestsPerMinute` | `600` | | Per client IP (the web server is one IP) |
| `Database:MigrateOnStartup` | `true` | | Honoured in Development only |
| `Cors:AllowedOrigins:N` | `https://localhost:7002` | | Origins allowed to call the token endpoint from a browser (Development: the API reference page). Empty elsewhere |

## Starter.Api

| Key | Notes |
| --- | --- |
| `ConnectionStrings:Default` / `Migrations` | Schema `app` |
| `Authentication:Issuer` | Must equal the identity server's issuer exactly (including the trailing `/`) |
| `RateLimiting:RequestsPerMinute` / `WritesPerMinute` | Per user |
| `OpenApi:Enabled` | `true` in Development: serves `/openapi/v1.json` and the API reference at `/scalar` |
| `OpenApi:PublicUrl` | Public URL of the API, used for the reference page's sign-in redirect (`<PublicUrl>scalar/`) |

## Starter.Web

| Key | Notes |
| --- | --- |
| `ConnectionStrings:Default` / `Migrations` | Schema `web` |
| `Authentication:Authority` | Identity server URL |
| `Authentication:ClientId` | `starter-web` |
| `Authentication:ClientSecret` | Secret; must match `OpenIddict:Clients:starter-web:ClientSecret` |
| `Api:BaseUrl` | API URL |

## Logging (all three applications)

Section `ActivityLogging` — destination (`File`, `Elasticsearch`, `Both`), categories (`Errors`, `Security`, `Api`,
`Frontend`), retention, file path and Elasticsearch connection (`Url`, `ApiKey` is a secret, `Namespace`).
Docker Compose reads `LOG_DESTINATION` and `LOG_RETENTION_DAYS` from `.env`. Details: [logging.md](logging.md).

## Behind a reverse proxy

Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (App Service does this for you when set in app settings) so the apps
see the original scheme (https) and client IP. Restrict `AllowedHosts` to your host names in production.

## Email

Locally all mail goes to Mailpit (http://localhost:8025), **not** to the real address. To deliver real emails during development, point `Email:Host` etc. at a real SMTP account (for Gmail: `smtp.gmail.com`, port 587, `StartTls`, your address as `Email:UserName` and an app password as `Email:Password`, both in user secrets) and remove `Email:LocalInboxUrl`. In production use any SMTP relay with TLS, for example
Azure Communication Services Email (`smtp.azurecomm.net`, port 587, StartTls), SendGrid or Microsoft 365.
If sending fails, the account action still succeeds, the error is logged, and the user can request the email again.
