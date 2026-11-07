namespace Starter.Contracts.Notes;

/// <summary>Size limits enforced by both the UI (for early feedback) and the API (as the source of truth).</summary>
public static class NoteLimits
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 10_000;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
}
