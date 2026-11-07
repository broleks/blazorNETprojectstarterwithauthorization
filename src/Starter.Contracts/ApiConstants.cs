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
}
