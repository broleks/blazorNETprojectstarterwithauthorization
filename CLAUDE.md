# Notes for AI assistants working in this repository

- Read `README.md` and `docs/architecture.md` before changing authentication, tokens or sessions.
- Frontend work (Blazor components in `src/Starter.Web`, account pages in `src/Starter.Identity/Pages`):
  follow `design/STYLE-PROMPT.md` and reuse the tokens and classes from `src/Starter.Ui/wwwroot/theme.css`.
- Code, comments, documentation and UI text are in English.
- Keep the structure simple: feature folders, minimal APIs, EF Core directly. No MediatR, AutoMapper,
  generic repositories or extra layers.
- Never put secrets in tracked files. Local secrets live in `.env`, `.certs/` and user secrets.
- New API endpoints go under the `/api` group (protected by default); take the user id from the token
  (`ClaimsPrincipal.GetUserId()`), never from the request.
- After changing an EF Core model, add a migration (`dotnet ef migrations add <Name> --project src/Starter.<App> --output-dir Data/Migrations`).
- Run `dotnet test --project tests/Starter.Api.Tests` (needs Docker) before finishing; run the E2E tests for UI changes
  (see `docs/testing.md`).
