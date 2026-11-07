namespace Starter.Contracts;

/// <summary>A single page of results. Lists are always paged so responses stay small.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public bool HasNextPage => Page * PageSize < TotalCount;
}
