# Starter: Blazor Server + OpenIddict + protected API

A reusable starting point for .NET 10 web applications. Clone it (or generate a renamed copy with
`dotnet new`) and start building features — sign-up, sign-in, sessions, API security, database,
containers and tests are already in place.

| What | How |
| --- | --- |
| UI | Blazor Web App, Interactive Server (`Starter.Web`) |
| Accounts | ASP.NET Core Identity — email + password, email confirmation, password reset, Google |
| Tokens | OpenIddict (OAuth 2.0 / OpenID Connect), Authorization Code + PKCE (`Starter.Identity`) |
| API | ASP.NET Core minimal APIs, JWT validation, per-user data (`Starter.Api`) |
| Data | EF Core, SQL Server locally, Azure SQL in production |
| Local run | Docker Compose (SQL Server, Mailpit, optionally the apps) or `dotnet run` / Visual Studio |
| Tests | Integration tests with the real auth stack (`Starter.Api.Tests`), Playwright E2E (`Starter.E2E.Tests`) |
| Delivery | Multi-stage Dockerfiles, GitHub Actions CI, Azure App Service + Azure SQL (Bicep) |

## Contents

- [Solution structure](#solution-structure)
- [Quick start](#quick-start)
- [How sign-in and API calls work](#how-sign-in-and-api-calls-work)
- [Where to find things](#where-to-find-things)
- [Running the tests](#running-the-tests)
- Detailed documentation in [`docs/`](docs):
  [architecture](docs/architecture.md) ·
  [security](docs/security.md) ·
  [local development](docs/local-development.md) ·
  [configuration](docs/configuration.md) ·
  [Google sign-in](docs/google-sign-in.md) ·
  [extending the starter](docs/extending.md) ·
  [testing](docs/testing.md) ·
  [Azure deployment](docs/azure-deployment.md) ·
  [maintenance and new apps](docs/maintenance.md)
- UI style: [`design/STYLE-PROMPT.md`](design/STYLE-PROMPT.md)

## Solution structure

```
src/
  Starter.Identity    Identity server: accounts (ASP.NET Core Identity), OpenIddict, account pages (Razor Pages), Google
  Starter.Api         Web API: validates access tokens, owns business data (notes module)
  Starter.Web         Blazor Web App (Interactive Server): UI, OpenID Connect client, server-side sessions
  Starter.Contracts   Request/response models shared by the API and its clients
  Starter.Ui          Shared stylesheet (theme.css) used by the app and the account pages
tests/
  Starter.Api.Tests   Integration and security tests: real identity server + API in memory, SQL Server in a container
  Starter.E2E.Tests   Playwright (xUnit v3) end-to-end tests against a running environment
deploy/
  sql/                Database, schemas and least-privilege users (local SQL Server and Azure SQL)
  azure/              Bicep template for App Service, Azure SQL, Key Vault, registry, Application Insights
scripts/
  setup-local.cs      Creates local secrets, certificates and user secrets (run with `dotnet run`)
```

**Why a separate identity server?** It is the only component that sees passwords, talks to Google and
issues tokens. The web app and a future mobile app are both ordinary OpenID Connect clients of it, and the
API contains no UI and no cookies — it only validates tokens. See [architecture](docs/architecture.md).

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), Docker (Docker Desktop on
Windows/macOS), and once per machine:

```bash
dotnet dev-certs https --trust
```

(On Linux this command cannot configure every browser and tool; see
[local development → HTTPS](docs/local-development.md#https-and-certificates).)

Create local secrets (`.env`, `.certs/`, user secrets — all git-ignored):

```bash
dotnet run scripts/setup-local.cs
```

### Variant 1 — everything in Docker

```bash
docker compose --profile app up -d --build --wait
```

### Variant 2 — database and mail in Docker, apps in Visual Studio or `dotnet run`

```bash
docker compose up -d --wait
```

Then start the three projects — in Visual Studio choose *Configure Startup Projects → Multiple startup
projects* (`Starter.Identity`, `Starter.Api`, `Starter.Web`, profile `https`), or in three terminals:

```bash
dotnet run --project src/Starter.Identity --launch-profile https
```

```bash
dotnet run --project src/Starter.Api --launch-profile https
```

```bash
dotnet run --project src/Starter.Web --launch-profile https
```

In Development the apps apply their database migrations at startup.

### Open the app

| URL | What |
| --- | --- |
| https://localhost:7003 | The application (Starter.Web) |
| https://localhost:7001 | Account pages / identity server (Starter.Identity) |
| https://localhost:7002/health/ready | API health (API endpoints need a token) |
| http://localhost:8025 | Mailpit — confirmation and password reset emails land here |

Both variants use the same addresses, so OAuth redirect URIs and the Google callback URL are identical.
Stop everything with `docker compose --profile app down` (add `--volumes` to delete the database).

## How sign-in and API calls work

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant W as Starter.Web (Blazor Server)
    participant I as Starter.Identity (OpenIddict)
    participant A as Starter.Api
    B->>W: GET /notes (no session)
    W-->>B: 302 to /connect/authorize (code + PKCE)
    B->>I: sign in (password or Google)
    I-->>B: 302 to Web /signin-oidc?code=...
    B->>W: code
    W->>I: exchange code + PKCE verifier + client secret (server to server)
    I-->>W: access token (5 min), refresh token, id token
    W->>W: store tokens encrypted in the server-side session (SQL)
    W-->>B: __Host- session cookie (only a random session key)
    B->>W: UI interaction over the Blazor circuit (WebSocket)
    W->>A: API call with Bearer access token (added per request on the server)
    A->>A: validate signature, issuer, audience, expiry, scope; owner = sub
    A-->>W: data
```

- The browser never sees a token: it holds an HttpOnly, `Secure`, `__Host-` cookie with a random session key.
- Blazor components get the session key from the authentication state, not from `HttpContext` (which is not
  available inside an interactive circuit), load the token from the session store, refresh it when it is about
  to expire, and attach it to each individual API request.
- Sign-out deletes the server-side session, revokes the refresh token and ends the identity server session.
  An access token that was already issued stays valid until it expires (at most 5 minutes) but exists only on
  the server. Details and other limits: [security](docs/security.md#limits-of-this-design).

## Where to find things

| Topic | Location |
| --- | --- |
| Token issuing, scopes, lifetimes, keys | `src/Starter.Identity/Infrastructure/OpenIddictSetup.cs` |
| Authorize / token / end-session logic | `src/Starter.Identity/Endpoints/AuthorizationEndpoints.cs` |
| Identity options, cookies, Google | `src/Starter.Identity/Infrastructure/IdentitySetup.cs` |
| Account pages | `src/Starter.Identity/Pages/Account/**` |
| Client registrations (seeded from config) | `src/Starter.Identity/Infrastructure/DatabaseSetup.cs` |
| API authentication and authorization policy | `src/Starter.Api/Infrastructure/AuthenticationSetup.cs` |
| Example feature (notes) | `src/Starter.Api/Features/Notes/` |
| Web OpenID Connect client, sign-in/out endpoints | `src/Starter.Web/Infrastructure/Authentication/AuthenticationSetup.cs` |
| Server-side session store | `src/Starter.Web/Infrastructure/Sessions/ServerSideTicketStore.cs` |
| Token refresh for circuits | `src/Starter.Web/Infrastructure/Authentication/SessionTokenService.cs` |
| Calling the API from components | `src/Starter.Web/Infrastructure/Api/ApiClient.cs`, `NotesApi.cs` |
| Pages and layout | `src/Starter.Web/Components/**` |
| Theme | `src/Starter.Ui/wwwroot/theme.css`, `src/Starter.Web/wwwroot/css/app.css` |

How to add a protected page, an API endpoint and a new entity: [docs/extending.md](docs/extending.md).

## Running the tests

```bash
dotnet test --project tests/Starter.Api.Tests
```

Integration and security tests; needs Docker (SQL Server is started with Testcontainers).

```bash
dotnet test --project tests/Starter.E2E.Tests
```

Playwright tests; needs a running environment (variant 1 or 2) and the Playwright browser
(`pwsh tests/Starter.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` after the first build).
Reports, traces and options: [docs/testing.md](docs/testing.md).

## Status and limits

The starter is verified locally in both variants (build, 60 integration tests, 15 Playwright tests, container
images). The Azure template compiles but has not been deployed by the authors of this repository — treat
[docs/azure-deployment.md](docs/azure-deployment.md) as a reviewed plan, not a tested release. Multi-instance
operation is designed for (shared Data Protection keys and sessions in SQL) but not verified; see the scaling
section there. Not included on purpose: admin panel, roles, two-factor authentication, account deletion.
