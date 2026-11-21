# New applications and maintenance

## Create a new application from the starter

### 1. Get a copy

Either clone the repository and keep the `Starter` names, or generate a renamed copy with the `dotnet new` template
(replaces `Starter` everywhere: namespaces, projects, solution, cookie names, database and image names; gives each
project new user-secret ids). Install the template once, from a clone of this repository:

```bash
dotnet new install .
```

```bash
dotnet new blazor-oidc-starter -n Acme -o ../Acme
```

Initialise git in the new folder and record the starter commit you started from (e.g. in the README) — you will need
it to port later starter fixes.

### 2. Run it unchanged

```bash
dotnet run scripts/setup-local.cs
```

```bash
docker compose up -d --wait
```

Start Identity, Api and Web (README, variant 2), register an account, confirm it in Mailpit, sign in. Run
`dotnet test --project tests/Starter.Api.Tests` once. Now you know the base works on your machine.

### 3. Configure, don't modify, the sign-in service

Per application you set only configuration of `Starter.Identity`:

- application name shown on the account pages and in emails (`Email:FromName`, page texts if needed),
- client registration: redirect URIs and secret (`OpenIddict:Clients`), done locally by the setup script,
- Google credentials, if wanted ([google-sign-in.md](google-sign-in.md)),
- production URLs, SMTP and certificates ([azure-deployment.md](azure-deployment.md)).

Leave the code of the stable modules unchanged (listed in the README section *Working model* and in `CLAUDE.md`).

### 4. Build your first feature next to the notes example

Follow [extending.md](extending.md): contracts in `Starter.Contracts/<Feature>`, entity and endpoints in
`Starter.Api/Features/<Feature>`, a migration, a typed client and pages in `Starter.Web`, an ownership test in
`Starter.Api.Tests`. Keep the notes module until then — it is the working example to copy from.

### 5. Remove the notes example

When your own feature works, delete the example:

| Where | What |
| --- | --- |
| `src/Starter.Api/Features/Notes/`, `Features/Dashboard/` | Endpoints and entity; remove their `Map...` calls in `Program.cs` and the `Notes` `DbSet` in `Infrastructure/AppDbContext.cs` |
| `src/Starter.Api/Data/Migrations/` | Add a migration that drops the `Notes` table (`dotnet ef migrations add RemoveNotes ...`); do not delete existing migrations once a database has used them |
| `src/Starter.Contracts/Notes/`, `Dashboard/` | Contracts |
| `src/Starter.Web/Components/Pages/` | `Notes.razor`, `NoteEditor.razor`, `Archive.razor`, `Dashboard.razor` (replace with your home page at `/`) |
| `src/Starter.Web/Components/Shared/` | `NoteList.razor`, `NotesPage.razor` |
| `src/Starter.Web/Infrastructure/Api/NotesApi.cs` | Typed client of the example, and its registration in `Program.cs` (keep `AccountApi`) |
| `src/Starter.Web/Components/Layout/NavMenu.razor` | Menu items |
| `tests/Starter.Api.Tests/Notes/` | Notes tests (keep the security tests in `Security/` and `Identity/`; point the few that call `/api/notes` at your endpoints) |
| `tests/Starter.E2E.Tests/Tests/NotesTests.cs` | Notes scenarios; replace `CreateNoteAsync` in `E2ETestBase` and adapt `MobileTests` and the cleanup SQL in `TestDataCleanup` |

Build, run both test projects, and update `README.md` and `design/STYLE-PROMPT.md` for your application.

## Keep the link to the starter

Record the starter commit you started from (e.g. in the new README). To bring later starter fixes over:

```bash
git remote add starter <starter repository url>
```

```bash
git fetch starter
```

Review the starter's commits since your base (`git log <base>..starter/main`) and apply relevant ones with
`git cherry-pick -x <commit>`; with renamed namespaces, apply the diff manually (`git show <commit>`). Fixes to
security-relevant files (`AuthenticationSetup.cs`, `OpenIddictSetup.cs`, `AuthorizationEndpoints.cs`,
`ServerSideTicketStore.cs`, `SessionTokenService.cs`, `ApiClient.cs`) are the important ones to port. Keep these
files close to the starter's version to make that easy.

## Update dependencies

- All package versions are in `Directory.Packages.props` (central package management, transitive pins included).
  There are no `packages.lock.json` files: implicit SDK packages follow the installed SDK patch and would break locked restores.
- Dependabot opens weekly grouped pull requests (NuGet, Docker base images, GitHub Actions).

Manual update:

1. Change versions in `Directory.Packages.props` (keep ASP.NET Core / EF Core on the same patch: `AspNetCoreVersion`).
2. `dotnet restore Starter.slnx`.
3. Read release notes of OpenIddict and ASP.NET Core for security or breaking changes.
4. `dotnet list Starter.slnx package --vulnerable --include-transitive`; pin fixed transitive versions in the
   *Security pins* group if needed.
5. Build, run the integration tests and the E2E tests; rebuild the images (`docker compose --profile app build --pull`).
6. Update `global.json` when moving to a new SDK feature band; update the `dotnet-ef` tool version in
   `.config/dotnet-tools.json` together with EF Core.

Major versions (e.g. OpenIddict 8, .NET 11): do them on a branch, follow the vendor migration guides, and add a
migration if the OpenIddict or Identity schema changes.
