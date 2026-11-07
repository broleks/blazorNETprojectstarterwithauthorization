using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Starter.Identity.Data;
using Starter.Identity.Endpoints;
using Starter.Identity.Infrastructure;
using Starter.Identity.Infrastructure.Email;

if (args is ["--health-check", var healthUrl])
{
    return await HealthCheckCommand.RunAsync(healthUrl);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddAuthDatabase();
builder.AddAccounts();
builder.AddAuthorizationServer();
builder.AddAccountRateLimiting();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Account/Manage");
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PublicUrls>();
builder.Services.AddScoped<AccountSecurity>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--migrate"))
{
    await DatabaseSetup.MigrateAndSeedAsync(app.Services, app.Configuration, app.Logger);
    return 0;
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DatabaseSetup.MigrateAndSeedAsync(app.Services, app.Configuration, app.Logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");
app.UseHttpsRedirection();

var googleEnabled = (await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(ExternalProviders.Google)) is not null;
app.UseSecurityHeaders(app.Services.GetRequiredService<PublicUrls>(), googleEnabled);

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapAuthorizationEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
return 0;

