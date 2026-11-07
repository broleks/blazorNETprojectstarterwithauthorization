# Testing

Two test projects, both xUnit v3 on Microsoft.Testing.Platform (`global.json` opts `dotnet test` into it).

| Project | What it tests | Needs |
| --- | --- | --- |
| `Starter.Api.Tests` | API and identity server security and behaviour, in memory | Docker (SQL Server via Testcontainers) |
| `Starter.E2E.Tests` | The whole system through the browser with Playwright | A running environment (Compose or `dotnet run`) + Playwright browser |

## Integration and security tests

```bash
dotnet test --project tests/Starter.Api.Tests
```

- Starts SQL Server in a container (or uses `STARTER_TEST_SQL`, a server connection string, e.g. in CI with a
  service container), creates a fresh database per run, and hosts the **real** Starter.Identity and Starter.Api.
- Tokens are obtained through the real flow: authorize → login form with antiforgery token → code → token
  endpoint with PKCE and the client secret. The API validates them with its normal configuration (keys from the
  identity server's discovery document). There is no test authentication handler.
- Negative cases the identity server never produces (expired token, wrong audience/issuer, missing scope, unknown
  key) use hand-minted JWTs, with a positive control that a correctly minted token is accepted.

Covered: no/invalid/tampered/expired tokens, wrong audience or issuer, unknown signing key, missing scope (403),
id token used as access token, foreign notes (read/update/delete → 404, data unchanged), owner injection (400),
validation and size limits, paging limits, owner CRUD + archive/restore + paging, refresh rotation, revocation,
password change and security stamp invalidation, deleted account, rate limiting (API and account forms),
email links not shown on pages and built from the public URL, enumeration-safe registration, antiforgery,
open redirect protection, PKCE requirement, unregistered redirect URI, Google button/challenge/cancel/forged state.

## End-to-end tests (Playwright)

1. Start an environment (README, variant 1 or 2).
2. Build and install the browser once:

   ```bash
   dotnet build tests/Starter.E2E.Tests
   ```

   ```bash
   pwsh tests/Starter.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

3. Run:

   ```bash
   dotnet test --project tests/Starter.E2E.Tests --report-xunit-html --results-directory TestResults/e2e
   ```

Scenarios: anonymous redirect, registration with display name, email confirmation via Mailpit, sign-in, wrong
password, sign-out, password reset, display name change, notes CRUD with delete confirmation, archive/restore,
dashboard data, isolation of two users, API error messages and validation, phone-size navigation, Google button
hidden without configuration.

### Settings (environment variables)

| Variable | Default | Purpose |
| --- | --- | --- |
| `E2E_WEB_URL` / `E2E_IDENTITY_URL` / `E2E_MAILPIT_URL` | local addresses | Target environment |
| `E2E_BROWSER` | `chromium` | `firefox` or `webkit` (install that browser first) |
| `E2E_BROWSER_EXECUTABLE` | — | Use an installed browser instead of Playwright's download |
| `E2E_HEADED` | — | `true` to watch the browser |
| `E2E_IGNORE_HTTPS_ERRORS` | `false` | Only for throw-away environments whose browser does not trust the dev certificate (CI) |
| `E2E_SQL` | — | Connection string of the **test** database; enables deleting the test accounts after the run |
| `E2E_ARTIFACTS` | `bin/.../TestResults/playwright` | Screenshots and traces of failed tests |

### Test data and artifacts

- Every test registers its own accounts (`e2e-<guid>@e2e.starter.test`); tests never share data and can run in
  parallel (4 at a time, `xunit.runner.json`).
- After the run, the test emails are deleted from Mailpit and — if `E2E_SQL` is set — the accounts and their notes,
  sessions and tokens. This is done by the test project, not by an endpoint in the application.
- Failed tests save a screenshot and a Playwright trace (`npx playwright show-trace <zip>` or
  https://trace.playwright.dev). Traces contain everything typed, cookies included: keep them out of git (ignored)
  and do not share traces from non-test environments.
- No `Sleep`: Playwright's auto-waiting assertions; the only polling is the mailbox wait with a deadline.

### Selectors

Roles and labels first (`GetByRole`, `GetByLabel`), `data-testid` only for values without a natural label
(statistics, toasts, alerts).

## CI

`.github/workflows/ci.yml`: restore (locked), build, EF model/migration check, vulnerable package check, integration
tests, image build + non-root check + Trivy scan per app, and an E2E job that creates fresh secrets, starts the full
Compose stack, runs Playwright and uploads reports, traces and container logs.
