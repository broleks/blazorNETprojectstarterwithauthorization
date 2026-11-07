# Local development

## One-time setup

1. Install the .NET 10 SDK and Docker.
2. Trust the ASP.NET Core development certificate: `dotnet dev-certs https --trust`.
3. From the repository root: `dotnet run scripts/setup-local.cs`.

The setup script creates (all git-ignored):

| File | Purpose |
| --- | --- |
| `.env` | Random SQL passwords (sa, migrator, one per app), the web client secret, certificate password, Google settings (empty) |
| `.certs/localhost.pfx` | Export of the development certificate, used by Kestrel inside the containers |
| `.certs/trust/localhost.crt` | Its public part, trusted by the containers for server-to-server HTTPS |
| `.certs/openiddict-signing.pfx`, `openiddict-encryption.pfx` | Token signing and encryption certificates |
| User secrets of the three projects | Connection strings and secrets for `dotnet run` / Visual Studio |

Run it again at any time; it only adds what is missing. `-- --force` regenerates everything
(then recreate the SQL volume: `docker compose down --volumes`).

## Variant 1: everything in Docker Compose

```bash
docker compose --profile app up -d --build --wait
```

Order of start-up: SQL Server (health check) → `db-setup` (database, schemas, users) → `*-migrate` jobs
(EF Core migrations with the migrator login, OpenIddict client registration) → identity, API, web (each with a
readiness health check). Logs: `docker compose --profile app logs -f web`.

## Variant 2: infrastructure in Docker, apps from Visual Studio / `dotnet run`

```bash
docker compose up -d --wait
```

Then start `Starter.Identity`, `Starter.Api`, `Starter.Web` with the `https` launch profile (in Visual Studio:
*Multiple startup projects*). In `Development` they migrate their own schema at startup.

## Addresses

The same in both variants: Web `https://localhost:7003`, Identity `https://localhost:7001`,
API `https://localhost:7002`, Mailpit `http://localhost:8025` (SMTP on 1025), SQL Server `localhost,14333`.
All ports are bound to 127.0.0.1.

In variant 1 the three app containers share one network namespace (the `localhost` service), so inside the
containers `https://localhost:7001` reaches the identity server exactly as it does in the browser. That keeps the
token issuer, redirect URIs and the Google callback identical in both variants.

Note: browsers do not separate cookies by port, so on `localhost` the three apps see each other's cookies.
The cookie names differ, and in production each app has its own host name.

## HTTPS and certificates

- Kestrel uses the ASP.NET Core development certificate (CN=localhost) in both variants.
- **Windows/macOS:** `dotnet dev-certs https --trust` makes browsers and .NET trust it.
- **Linux:** the command trusts the certificate for .NET via a directory it prints (usually
  `~/.aspnet/dev-certs/trust`) and asks you to set `SSL_CERT_DIR` to include it, e.g.
  `export SSL_CERT_DIR=$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs`. Browsers may need the certificate
  imported manually.
- **Containers** trust `.certs/trust` through `SSL_CERT_DIR=/https/trust:/etc/ssl/certs` (set in `compose.yaml`).
  Nothing in the application code disables certificate validation.

## OpenID Connect and Google addresses

| Purpose | Value (both variants) |
| --- | --- |
| Web redirect URI | `https://localhost:7003/signin-oidc` |
| Web post-logout redirect URI | `https://localhost:7003/signout-callback-oidc` |
| Google authorized redirect URI | `https://localhost:7001/signin-google` |
| Google authorized JavaScript origin | not needed (server-side flow) |

## Database access

Connect with any SQL client to `localhost,14333` as `sa` with `MSSQL_SA_PASSWORD` from `.env`. The data is kept in
the `sqlserver-data` volume; `docker compose down --volumes` deletes it.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `UntrustedRoot` / SSL errors between apps (variant 2, Linux) | Set `SSL_CERT_DIR` as above, or run `dotnet dev-certs https --trust` |
| "Too many attempts" while testing | Raise `ACCOUNT_POSTS_PER_MINUTE` in `.env` (Compose) or `RateLimiting:AccountPostsPerMinute` |
| Signed out after restarting containers | Expected only if `.certs/openiddict-*.pfx` changed (tokens are signed with them) |
| Login loop / "We could not sign you in" | Check that Identity's `OpenIddict:Clients:starter-web` redirect URI and client secret match the web app |
| No email | Open Mailpit at http://localhost:8025; check Identity logs for "could not be sent" |
