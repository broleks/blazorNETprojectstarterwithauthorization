using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Starter.Web.Components;
using Starter.Web.Infrastructure;
using Starter.Web.Infrastructure.Api;
using Starter.Web.Infrastructure.Authentication;
using Starter.Web.Infrastructure.Sessions;
using Starter.Web.Services;

if (args is ["--health-check", var healthUrl])
{
    return await HostingSetup.RunHealthCheckAsync(healthUrl);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddWebDatabase();
builder.AddWebAuthentication();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(TimeProvider.System);

var apiBaseUrl = new Uri(builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("'Api:BaseUrl' (the Starter.Api URL) is not configured."));
var identityUrl = new Uri(builder.Configuration["Authentication:Authority"]!);

// One HttpClient definition for the API. It has no default Authorization header: ApiClient adds the
// current user's token to each request message.
builder.Services.AddHttpClient(ApiClient.HttpClientName, client =>
{
    client.BaseAddress = apiBaseUrl;
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("health", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<NotesApi>();
builder.Services.AddScoped<ToastService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<WebDbContext>("database", tags: ["ready"])
    .AddTypeActivatedCheck<HttpDependencyHealthCheck>("identity", failureStatus: null, tags: ["ready"],
        new Uri(identityUrl, ".well-known/openid-configuration"))
    .AddTypeActivatedCheck<HttpDependencyHealthCheck>("api", failureStatus: null, tags: ["ready"],
        new Uri(apiBaseUrl, "health/live"));

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseSecurityHeaders(identityUrl.GetLeftPart(UriPartial.Authority));

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthenticationEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
return 0;
