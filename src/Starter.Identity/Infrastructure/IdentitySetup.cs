using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Starter.Identity.Data;

namespace Starter.Identity.Infrastructure;

/// <summary>ASP.NET Core Identity, cookies, Google and Data Protection for the identity server.</summary>
internal static class IdentitySetup
{
    public static void AddAccounts(this WebApplicationBuilder builder)
    {
        builder.Services.AddDataProtection()
            .SetApplicationName("Starter.Identity")
            // Keys are stored in the database so that cookies and email tokens survive restarts and
            // are shared by all instances. See docs/azure-deployment.md for encrypting them with Key Vault.
            .PersistKeysToDbContext<AuthDbContext>();

        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Length matters more than composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        // Email confirmation and password reset links expire after a few hours.
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(4));

        // How quickly other sessions of the same user notice a password change (security stamp check).
        builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        builder.Services.ConfigureApplicationCookie(options =>
        {
            // __Host- prefix: the browser only accepts the cookie over HTTPS, for this exact host and path "/".
            options.Cookie.Name = "__Host-starter-identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });

        builder.Services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "__Host-starter-external";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-starter-identity-af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        AddGoogleIfConfigured(builder);
    }

    /// <summary>
    /// Google is registered only when a client id and secret are configured. Without them the Google button
    /// is not rendered at all (the login page lists registered external schemes), so the UI never suggests
    /// that Google sign-in works when it does not.
    /// </summary>
    private static void AddGoogleIfConfigured(WebApplicationBuilder builder)
    {
        var clientId = builder.Configuration["Authentication:Google:ClientId"];
        var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        builder.Services.AddAuthentication().AddGoogle(options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            // Needed to decide whether an account created from Google can skip email confirmation.
            options.ClaimActions.MapJsonKey(ExternalLoginClaims.EmailVerified, "verified_email");
            options.ClaimActions.MapJsonKey(ExternalLoginClaims.EmailVerified, "email_verified");

            options.Events.OnAccessDenied = context =>
            {
                // The user pressed "Cancel" on Google's consent screen.
                context.Response.Redirect(ExternalLoginClaims.LoginErrorUrl(context.Properties, "cancelled"));
                context.HandleResponse();
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect(ExternalLoginClaims.LoginErrorUrl(context.Properties, "failed"));
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }
}

internal static class ExternalLoginClaims
{
    public const string EmailVerified = "urn:starter:email_verified";
    public const string ReturnUrlItem = "starter_return_url";

    public static string LoginErrorUrl(AuthenticationProperties? properties, string error)
    {
        var returnUrl = properties?.Items.TryGetValue(ReturnUrlItem, out var value) == true ? value : null;
        return "/Account/Login" + QueryString.Create(new Dictionary<string, string?>
        {
            ["externalError"] = error,
            ["returnUrl"] = returnUrl ?? "/",
        }).ToUriComponent();
    }

    public static bool IsEmailVerified(System.Security.Claims.ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirst(EmailVerified)?.Value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Name of the Google scheme, kept in one place for pages and tests.</summary>
internal static class ExternalProviders
{
    public const string Google = GoogleDefaults.AuthenticationScheme;
}
