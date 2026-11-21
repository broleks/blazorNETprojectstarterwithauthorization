# Notes for AI assistants working in this repository

## Working model: the sign-in modules are stable

Applications are created from this starter and then grown in two places only:

- `src/Starter.Web` — pages and components (`Components/**`, typed API clients in `Infrastructure/Api/*Api.cs`)
- `src/Starter.Api` + `src/Starter.Contracts` — business features (`Features/<Name>/`, contracts per feature)

The following are **stable modules**. Do not change their code as part of feature work:

- the whole `src/Starter.Identity` project (accounts, OpenIddict, Google, account pages)
- `src/Starter.Web/Infrastructure/Authentication/**` and `src/Starter.Web/Infrastructure/Sessions/**`
- `src/Starter.Web/Infrastructure/Api/ApiClient.cs`, `ApiResult.cs` and `AccountApi.cs`
- `src/Starter.Api/Infrastructure/AuthenticationSetup.cs`
- the whole `src/Starter.Observability` project (logging)

Configuration of these modules (appsettings, user secrets, environment variables, client registrations,
Google credentials) may change per application.

If a task really needs a change in a stable module (e.g. extra registration fields, roles, two-factor
authentication), stop and say so before editing: explain why, keep the change minimal and self-contained,
and mention that it probably belongs in the starter repository too, so other applications get it.
Keeping these files identical to the starter is what makes porting starter fixes a clean `git cherry-pick`.

- Read `README.md` and `docs/architecture.md` before changing authentication, tokens or sessions.
- Logging: follow `docs/logging.md`. API calls are logged automatically; for new business or UI events use
  `logger.LogActivity(LogCategories.Api, "<feature>.<action>", ...)` or `FrontendActivity`. Never log passwords,
  tokens, email addresses (use `ActivityLogExtensions.MaskEmail`) or user-entered content.
- Frontend work (Blazor components in `src/Starter.Web`, account pages in `src/Starter.Identity/Pages`):
  follow `design/STYLE-PROMPT.md` and reuse the tokens and classes from `src/Starter.Ui/wwwroot/theme.css`.
- Code, comments, documentation and UI text are in English.
- Keep the structure simple: feature folders, minimal APIs, EF Core directly. No MediatR, AutoMapper,
  generic repositories or extra layers.
- Never put secrets in tracked files. Local secrets live in `.env`, `.certs/` and user secrets.
- Every feature that stores user data implements `IUserDataHandler` (export + delete, see `docs/accounts-and-admin.md`)
  and registers it in `src/Starter.Api/Program.cs`; a test enforces this for entities with `OwnerId`.
- Administrator-only endpoints use `AuthenticationSetup.AdminPolicy`; pages use `[Authorize(Roles = ApiConstants.AdminRole)]`.
- New API endpoints go under the `/api` group (protected by default); take the user id from the token
  (`ClaimsPrincipal.GetUserId()`), never from the request.
- After changing an EF Core model, add a migration (`dotnet ef migrations add <Name> --project src/Starter.<App> --output-dir Data/Migrations`).
- Run `dotnet test --project tests/Starter.Api.Tests` (needs Docker) before finishing; run the E2E tests for UI changes
  (see `docs/testing.md`).
