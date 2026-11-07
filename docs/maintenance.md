# New applications and maintenance

## Create a new application from the starter

Install the starter as a `dotnet new` template (once, from a clone of this repository):

```bash
dotnet new install .
```

Create the application (replaces `Starter` with your name everywhere: namespaces, projects, solution, cookie
names, database name, image names; gives each project new user-secret ids):

```bash
dotnet new blazor-oidc-starter -n Acme -o ../Acme
```

Then in the new folder: `git init`, `dotnet run scripts/setup-local.cs`, start one of the variants from the README,
and replace the notes module with your own features (keep it until your first feature exists — it is the example the
tests and docs refer to). Update `design/STYLE-PROMPT.md` and the README.

Alternative without the template engine: copy the repository (GitHub *Use this template*) and keep the `Starter`
names.

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

- All package versions are in `Directory.Packages.props`; restores are locked by `packages.lock.json`.
- Dependabot opens weekly grouped pull requests (NuGet, Docker base images, GitHub Actions).

Manual update:

1. Change versions in `Directory.Packages.props` (keep ASP.NET Core / EF Core on the same patch: `AspNetCoreVersion`).
2. `dotnet restore Starter.slnx --force-evaluate` to refresh the lock files.
3. Read release notes of OpenIddict and ASP.NET Core for security or breaking changes.
4. `dotnet list Starter.slnx package --vulnerable --include-transitive`; pin fixed transitive versions in the
   *Security pins* group if needed.
5. Build, run the integration tests and the E2E tests; rebuild the images (`docker compose --profile app build --pull`).
6. Update `global.json` when moving to a new SDK feature band; update the `dotnet-ef` tool version in
   `.config/dotnet-tools.json` together with EF Core.

Major versions (e.g. OpenIddict 8, .NET 11): do them on a branch, follow the vendor migration guides, and add a
migration if the OpenIddict or Identity schema changes.
