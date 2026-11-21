namespace Starter.Contracts;

/// <summary>
/// Names that the identity server, the API and the clients must agree on.
/// Changing a value here is a breaking change for already issued tokens and registered clients.
/// </summary>
public static class ApiConstants
{
    /// <summary>OAuth scope a client must request to call the API.</summary>
    public const string Scope = "starter_api";

    /// <summary>Audience (resource) stamped into access tokens meant for the API.</summary>
    public const string Audience = "starter-api";

    /// <summary>
    /// Scope of the identity server's own service client (client credentials, no user). It allows only the internal
    /// account endpoints of the API: deleting and exporting the data of a user whose account is deleted or exported.
    /// </summary>
    public const string AccountsScope = "starter_api.accounts";

    /// <summary>Role that can see the administration pages and endpoints.</summary>
    public const string AdminRole = "Admin";
}
