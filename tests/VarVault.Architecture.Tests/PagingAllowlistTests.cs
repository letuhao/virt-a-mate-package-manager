using System.Reflection;
using VarVault.Sdk.Modularity;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.Architecture.Tests;

/// <summary>
/// Prevents pagination regressions: every UI-facing SDK method that returns
/// <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> must be explicitly allowlisted as a bounded/summary or
/// legacy-compat API. New unbounded list surfaces should expose <see cref="PageResult{T}"/> instead.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public sealed class PagingAllowlistTests
{
    /// <summary>
    /// Allowlisted <c>Interface.Method</c> names. Bounded summaries, capped searches, legacy
    /// full-list compat shims (prefer the Page* APIs), and Library's OrderedSnapshot backbone.
    /// </summary>
    private static readonly HashSet<string> Allowlisted = new(StringComparer.Ordinal)
    {
        // Bounded metadata / summaries
        "IRepositoryService.ListAsync",
        "IProfileService.ListAsync",
        "IPresetService.ListAsync",
        "IHealthService.EncodingGroupsAsync",
        "ITrashQueryService.ListBackupsAsync",
        "ITagService.ListAsync",
        "IAliasService.ListAsync",
        "IAnalyticsService.SpaceByTypeAsync",
        "IAnalyticsService.SpaceByTierAsync",
        "ILibraryQueryService.GetCreatorsAsync",
        "ILibraryQueryService.GetCreatorCountsAsync",
        "ICommandPaletteService.SearchAsync",

        // Library A9 ordered-snapshot exception (virtualized, not a secondary pager)
        "ILibraryQueryService.GetOrderedIdsAsync",
        "ILibraryQueryService.GetByIdsAsync",

        // Import history is capped by `take`
        "IImportService.HistoryAsync",
        "IImportHistoryStore.RecentAsync",

        // Intake / one-folder classification (session-bounded)
        "IIntakeService.ClassifyFolderAsync",

        // Legacy full-list shims — UI must prefer the Page* methods; kept for export/compat
        "IMissingDepsQuery.GetMissingAsync",
        "IReclaimService.ExactGroupsAsync",
        "IReclaimService.NearDuplicateGroupsAsync",
        "IHealthService.IntegrityAsync",
        "IHealthService.MissingMetaAsync",
        "ITieringService.MisplacedAsync",
        "ITieringService.StaleVersionsAsync",
        "ITrashQueryService.ListAsync",
        "IProposalService.ListAsync",
        "IPresetService.MembersAsync",
        "IAnalyticsService.SpaceByCreatorAsync",
        "IActivityLog.GetRecentAsync",
        "ITagService.PackageIdsAsync",
    };

    [Fact]
    public void Ui_facing_list_methods_are_allowlisted_or_paged()
    {
        var sdk = typeof(IModule).Assembly;
        var offenders = new List<string>();

        foreach (var type in sdk.GetTypes().Where(t => t.IsInterface && t.Namespace is not null
                                                       && t.Namespace.StartsWith("VarVault.Sdk", StringComparison.Ordinal)))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!ReturnsReadOnlyListTask(method))
                    continue;

                var key = $"{type.Name}.{method.Name}";
                if (!Allowlisted.Contains(key))
                    offenders.Add(key);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "New UI-facing Task<IReadOnlyList<T>> methods must use PageResult<T> or be added to the bounded allowlist. " +
            $"Offenders: {string.Join(", ", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    [Fact]
    public void Allowlist_entries_still_exist()
    {
        var sdk = typeof(IModule).Assembly;
        var existing = sdk.GetTypes()
            .Where(t => t.IsInterface)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(ReturnsReadOnlyListTask)
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var stale = Allowlisted.Where(a => !existing.Contains(a)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(
            stale.Count == 0,
            $"Allowlist has stale entries (method removed/renamed): {string.Join(", ", stale)}");
    }

    private static bool ReturnsReadOnlyListTask(MethodInfo method)
    {
        if (method.ReturnType is not { IsGenericType: true } ret)
            return false;
        if (ret.GetGenericTypeDefinition() != typeof(Task<>))
            return false;
        var inner = ret.GetGenericArguments()[0];
        if (!inner.IsGenericType)
            return false;
        var def = inner.GetGenericTypeDefinition();
        return def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>) || def == typeof(List<>);
    }
}
