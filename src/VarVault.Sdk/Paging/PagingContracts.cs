namespace VarVault.Sdk.Paging;

/// <summary>Numbered page request for UI-facing queries.</summary>
public sealed record PageRequest(int PageNumber = 1, int PageSize = 50)
{
    public int SafePageNumber => Math.Max(1, PageNumber);
    public int SafePageSize => Math.Clamp(PageSize, 1, 100);
    public int Skip => (SafePageNumber - 1) * SafePageSize;

    public PageRequest Normalize() => new(SafePageNumber, SafePageSize);
}

/// <summary>One page of rows plus the exact total result count.</summary>
public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int SafePageNumber => Math.Max(1, PageNumber);
    public int SafePageSize => Math.Clamp(PageSize, 1, 100);
    public int PageCount => TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)SafePageSize);
    public int FirstItemNumber => TotalCount == 0 ? 0 : ((SafePageNumber - 1) * SafePageSize) + 1;
    public int LastItemNumber => TotalCount == 0 ? 0 : Math.Min(TotalCount, FirstItemNumber + Items.Count - 1);

    public static PageResult<T> Empty(PageRequest request) =>
        new([], 0, request.SafePageNumber, request.SafePageSize);
}
