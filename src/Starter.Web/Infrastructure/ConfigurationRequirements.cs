using Starter.Observability;

namespace Starter.Web.Infrastructure;

/// <summary>Everything Starter.Web needs to start, checked at once (see ConfigurationCheck).</summary>
internal static class ConfigurationRequirements
{
    public static void Check(ConfigurationCheck check) => check
        .Required("ConnectionStrings:Default", "SQL Server connection for the web schema (sessions)")
        .Url("Authentication:Authority", hint: "the URL of Starter.Identity")
        .Required("Authentication:ClientId")
        .Required("Authentication:ClientSecret", "must equal OpenIddict:Clients:starter-web:ClientSecret of Starter.Identity")
        .Url("Api:BaseUrl", hint: "the URL of Starter.Api");
}
