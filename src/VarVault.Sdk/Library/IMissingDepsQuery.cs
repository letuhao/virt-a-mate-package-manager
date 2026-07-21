using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Library;

/// <summary>A missing dependency ref, how many packages need it, and the owned var a global alias maps it to (if any). (doc 26 · F-9)</summary>
public sealed record MissingDependency(string Ref, int NeededByCount, string? AliasTarget = null);

/// <summary>Lists unresolved dependency refs with their needed-by counts, for the missing-deps screen. (2.14.)</summary>
public interface IMissingDepsQuery
{
    async Task<PageResult<MissingDependency>> GetPageAsync(
        PageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var all = await GetMissingAsync(cancellationToken).ConfigureAwait(false);
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? all
            : all.Where(x => x.Ref.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        var page = request.Normalize();
        return new PageResult<MissingDependency>(
            filtered.Skip(page.Skip).Take(page.SafePageSize).ToList(),
            filtered.Count,
            page.SafePageNumber,
            page.SafePageSize);
    }

    Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default);
}
