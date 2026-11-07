# Extending the starter

The notes module is the pattern. A feature consists of:

```
src/Starter.Contracts/<Feature>/        request/response records + validation attributes
src/Starter.Api/Features/<Feature>/     entity, EF configuration, endpoints
src/Starter.Web/Components/Pages/       pages; API calls in Infrastructure/Api
tests/Starter.Api.Tests/<Feature>/      ownership + validation + happy path tests
```

No MediatR, AutoMapper or repositories: endpoints use the `DbContext` directly and map with small methods.

## Add a new entity (example: `Task`)

1. **Contracts** — `src/Starter.Contracts/Tasks/TaskModels.cs`: `TaskSummary`, `CreateTaskRequest` (class with
   `[Required]`, `[StringLength]`), `UpdateTaskRequest`. Never include an owner or user id.
2. **Entity and configuration** — `src/Starter.Api/Features/Tasks/TaskItem.cs` with `OwnerId`, an
   `IEntityTypeConfiguration<TaskItem>` (max lengths, index starting with `OwnerId`, check constraints). It is picked
   up automatically by `ApplyConfigurationsFromAssembly`; add `public DbSet<TaskItem> Tasks => Set<TaskItem>();`
   to `AppDbContext`.
3. **Migration**:

   ```bash
   dotnet ef migrations add AddTasks --project src/Starter.Api --output-dir Data/Migrations
   ```

   Development applies it at startup; Compose and Azure apply it in the migration step.
4. **Endpoints** — `src/Starter.Api/Features/Tasks/TaskEndpoints.cs`, modelled on `NotesEndpoints`:
   every query `Where(t => t.OwnerId == user.GetUserId())`, 404 for missing or foreign ids,
   `RequireRateLimiting(RateLimitingSetup.WritePolicy)` on writes, paging with `[Range]` on query parameters.
5. **Register** in `src/Starter.Api/Program.cs`:

   ```csharp
   app.MapGroup("/api")
       .RequireAuthorization(AuthenticationSetup.ApiPolicy)
       ...
       .MapTaskEndpoints();
   ```

   Authentication and authorization come from the group — nothing to repeat per endpoint.
6. **Tests** — copy `tests/Starter.Api.Tests/Notes/NoteOwnershipTests.cs` and adapt.

## Add an API endpoint that is not about stored data

Add a `Map...` call inside an existing feature or a new `Features/<Name>/<Name>Endpoints.cs`. Read the user from the
injected `ClaimsPrincipal` (`user.GetUserId()`, `user.FindFirstValue("name")`). Only `/health/*` is anonymous;
anything else needs a token with the `starter_api` scope (fallback policy).

## Call it from the web app

Add methods to `src/Starter.Web/Infrastructure/Api/NotesApi.cs` or a new class like it, registered as scoped in
`Program.cs`:

```csharp
public sealed class TasksApi(ApiClient api)
{
    public Task<ApiResult<PagedResponse<TaskSummary>>> GetTasksAsync(int page) =>
        api.GetAsync<PagedResponse<TaskSummary>>($"api/tasks?page={page}");
}
```

`ApiClient` adds the user's token, refreshes it when needed and translates errors into `ApiResult`.

## Add a protected page

Create `src/Starter.Web/Components/Pages/Tasks.razor`:

```razor
@page "/tasks"
@inject TasksApi Api

<PageTitle>Tasks · Starter</PageTitle>
<h1>Tasks</h1>

@if (result is null) { <LoadingState /> }
else if (!result.Succeeded) { <ApiErrorAlert Result="result" OnRetry="LoadAsync" /> }
else { /* render result.Value */ }

@code {
    private ApiResult<PagedResponse<TaskSummary>>? result;
    protected override Task OnInitializedAsync() => LoadAsync();
    private async Task LoadAsync() { result = null; result = await Api.GetTasksAsync(1); }
}
```

Pages in `Components/Pages` require sign-in automatically (`_Imports.razor` there has `[Authorize]`); use
`@attribute [AllowAnonymous]` for public pages. Add the link to `Components/Layout/NavMenu.razor`. Follow
`design/STYLE-PROMPT.md` for the look.

## Add a client (e.g. a mobile app)

Add to the identity server configuration:

```json
"OpenIddict": {
  "Clients": {
    "starter-mobile": {
      "DisplayName": "Starter Mobile",
      "RedirectUris": [ "com.example.starter:/oauth/callback" ],
      "PostLogoutRedirectUris": [ "com.example.starter:/oauth/signout" ]
    }
  }
}
```

No secret → public client; PKCE is required for all clients. Run the identity migration step to register it. The
app uses the system browser (e.g. AppAuth / `WebAuthenticator`), requests `openid profile email offline_access starter_api`,
stores the refresh token in the platform's secure storage and calls the API with the access token.

## Roles or permissions later

Add roles with ASP.NET Core Identity (`AddRoles<IdentityRole>()`), emit a `role` claim with
`Destinations.AccessToken` in `AccountSecurity.GetDestinations`, and use authorization policies in the API.
