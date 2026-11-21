namespace GreenCarWash.BuildingBlocks.Pagination;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Pagination
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public static (int Page, int PageSize, int Skip) Normalize(int page, int pageSize)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = pageSize < 1
            ? DefaultPageSize
            : Math.Min(pageSize, MaxPageSize);

        return (normalizedPage, normalizedSize, (normalizedPage - 1) * normalizedSize);
    }

    public static PagedResult<T> Create<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        var (normalizedPage, normalizedSize, _) = Normalize(page, pageSize);
        return new PagedResult<T>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedSize,
            TotalCount = totalCount
        };
    }
}
