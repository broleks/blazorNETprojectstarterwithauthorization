using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Starter.Api.Features.Account;
using Starter.Api.Features.Admin;
using Starter.Api.Features.Dashboard;
using Starter.Api.Features.Notes;
using Starter.Api.Features.UserData;
using Starter.Api.Infrastructure;
using Starter.Observability;

if (args is ["--health-check", var healthUrl])
{
    return await HealthCheckCommand.RunAsync(healthUrl);
}

var builder = WebApplication.CreateBuilder(args);

// Requests are small JSON documents; anything larger is rejected before it reaches an endpoint.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 64 * 1024);

builder.AddStarterLogging("api");
builder.AddAppDatabase();
builder.AddApiAuthentication();
builder.AddApiRateLimiting();

builder.Services.AddSingleton(TimeProvider.System);

// GDPR: every feature with user data registers its export/delete handler (see Features/UserData).
builder.Services.AddScoped<IUserDataHandler, NotesUserData>();
// Errors are described in the API log (title, validation messages) and the response carries a traceId the user can quote.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = RequestLogging.CaptureProblemDetails);
builder.Services.AddValidation();
builder.AddApiDocumentation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Unknown properties are an error rather than silently ignored. This makes attempts to send
    // fields the API controls (owner, id, timestamps) visible as 400 Bad Request.
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--migrate"))
{
    await DatabaseSetup.MigrateAsync(app.Configuration, app.Logger);
    return 0;
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DatabaseSetup.MigrateAsync(app.Configuration, app.Logger);
}

// Errors are returned as RFC 9457 problem details without stack traces or exception messages.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseUserLogScope();
app.UseApiRequestLogging();
app.UseRateLimiter();
app.UseAuthorization();

// Liveness: the process responds. Readiness: the database is reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

// OpenAPI document and the interactive reference at /scalar (only when OpenApi:Enabled, i.e. Development).
app.MapApiDocumentation();

// Every endpoint under /api requires a valid access token with the API scope (see AuthenticationSetup).
// To add a feature: create Features/<Name>/<Name>Endpoints.cs and register it here.
app.MapGroup("/api")
    .RequireAuthorization(AuthenticationSetup.ApiPolicy)
    .MapMeEndpoints()
    .MapDashboardEndpoints()
    .MapNotesEndpoints()
    .MapAdminEndpoints();

// Called only by Starter.Identity's service client (account deletion and data export).
app.MapUserDataEndpoints();

await app.RunAsync();
return 0;

/// <summary>Marker used by integration tests to locate this assembly's entry point.</summary>
public partial class Program;
