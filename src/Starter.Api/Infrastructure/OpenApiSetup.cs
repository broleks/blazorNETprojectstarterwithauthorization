using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Starter.Contracts;

namespace Starter.Api.Infrastructure;

/// <summary>
/// OpenAPI document (/openapi/v1.json) and an interactive API reference (Scalar, /scalar) for development.
///
/// The document describes how to get a token (OAuth 2.0 Authorization Code + PKCE at Starter.Identity), so the
/// reference page can sign you in with a real account and call the API with a real access token. It uses its own
/// public client, "starter-api-docs", registered only in the Development configuration of Starter.Identity.
/// Enabled with <c>OpenApi:Enabled</c> (true in appsettings.Development.json, off everywhere else).
/// </summary>
internal static class OpenApiSetup
{
    public const string SecurityScheme = "OAuth2";
    public const string DocsClientId = "starter-api-docs";

    public static void AddApiDocumentation(this WebApplicationBuilder builder)
    {
        var issuer = builder.Configuration["Authentication:Issuer"];
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Starter API";
            document.Info.Description = "Every endpoint needs an access token from Starter.Identity with the 'starter_api' scope.";
            if (string.IsNullOrWhiteSpace(issuer))
            {
                return Task.CompletedTask;
            }

            var authority = new Uri(issuer);
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SecurityScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Sign in at Starter.Identity (Authorization Code with PKCE).",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(authority, "connect/authorize"),
                        TokenUrl = new Uri(authority, "connect/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            ["openid"] = "Sign in",
                            ["profile"] = "Display name in the token",
                            [ApiConstants.Scope] = "Call the Starter API",
                        },
                    },
                },
            };
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SecurityScheme, document)] = [ApiConstants.Scope],
            });
            return Task.CompletedTask;
        }));
    }

    public static void MapApiDocumentation(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            return;
        }

        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(options => options
                .WithTitle("Starter API")
                .AddPreferredSecuritySchemes(SecurityScheme)
                .AddAuthorizationCodeFlow(SecurityScheme, flow => flow
                    .WithClientId(DocsClientId)
                    .WithPkce(Pkce.Sha256)
                    // Must match the redirect URI registered for the docs client in Starter.Identity.
                    .WithRedirectUri(new Uri(new Uri(app.Configuration["OpenApi:PublicUrl"] ?? "https://localhost:7002/"), "scalar/").ToString())
                    .WithSelectedScopes(["openid", "profile", ApiConstants.Scope])))
            .AllowAnonymous();
    }
}
