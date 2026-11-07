namespace Starter.Contracts.Account;

/// <summary>The signed-in user as the API sees them, based only on the validated access token.</summary>
public sealed record MeResponse(string UserId, string DisplayName, string? Email, IReadOnlyList<string> Scopes);
