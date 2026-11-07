# Configuration

Settings come from `appsettings.json`, `appsettings.{Environment}.json`, user secrets (Development only),
environment variables (`Section__Key`) and, in Azure, App Service settings with Key Vault references.
**Secrets are never stored in `appsettings*.json`.**

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
| `Email:FromAddress`, `FromName`, `Host`, `Port` | | | SMTP relay |
| `Email:Security` | `StartTls` | | `None` only for local Mailpit |
| `Email:UserName` / `Password` | | yes | |
| `Email:MaxPerRecipientPerHour` | `5` | | |
| `RateLimiting:AccountPostsPerMinute` | `20` | | Per client IP |
| `RateLimiting:TokenRequestsPerMinute` | `600` | | Per client IP (the web server is one IP) |
| `Database:MigrateOnStartup` | `true` | | Honoured in Development only |

## Starter.Api

| Key | Notes |
| --- | --- |
| `ConnectionStrings:Default` / `Migrations` | Schema `app` |
| `Authentication:Issuer` | Must equal the identity server's issuer exactly (including the trailing `/`) |
| `RateLimiting:RequestsPerMinute` / `WritesPerMinute` | Per user |

## Starter.Web

| Key | Notes |
| --- | --- |
| `ConnectionStrings:Default` / `Migrations` | Schema `web` |
| `Authentication:Authority` | Identity server URL |
| `Authentication:ClientId` | `starter-web` |
| `Authentication:ClientSecret` | Secret; must match `OpenIddict:Clients:starter-web:ClientSecret` |
| `Api:BaseUrl` | API URL |

## Behind a reverse proxy

Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (App Service does this for you when set in app settings) so the apps
see the original scheme (https) and client IP. Restrict `AllowedHosts` to your host names in production.

## Email

Locally all mail goes to Mailpit. In production use any SMTP relay with TLS, for example
Azure Communication Services Email (`smtp.azurecomm.net`, port 587, StartTls), SendGrid or Microsoft 365.
If sending fails, the account action still succeeds, the error is logged, and the user can request the email again.
