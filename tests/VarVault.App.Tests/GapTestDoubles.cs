using System.Collections.Generic;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Settings;

namespace VarVault.App.Tests;

/// <summary>Records which dialog each screen asks to open (18-gap G-D/G-E reachability).</summary>
public sealed class FakeDialogLauncher : IDialogLauncher
{
    public List<string> Opened { get; } = [];
    public void OpenAddRepo() => Opened.Add("add-repo");
    public void OpenEditRepo(VarVault.Sdk.Repositories.RepositoryInfo repo, System.Action? onSaved = null) => Opened.Add($"edit-repo:{repo.Id}");
    public void OpenRescue() => Opened.Add("rescue");
    public void OpenOnboarding() => Opened.Add("onboarding");
    public void OpenMigratePlan() => Opened.Add("migrate");
    public void OpenVarDetail(long packageId) => OpenVarDetail(packageId, push: false);
    public void OpenVarDetail(long packageId, bool push) => Opened.Add($"var-detail:{packageId}{(push ? ":push" : "")}");
    public void OpenAlias(string missingRef, Action? onSaved = null, string? suggestedOwnedQuery = null) =>
        Opened.Add(string.IsNullOrWhiteSpace(suggestedOwnedQuery) ? $"alias:{missingRef}" : $"alias:{missingRef}:{suggestedOwnedQuery}");
    public void OpenManageAliases(Action? onChanged = null, string? focusMissingRef = null, string? suggestedOwnedQuery = null) =>
        Opened.Add(string.IsNullOrWhiteSpace(focusMissingRef)
            ? "manage-aliases"
            : string.IsNullOrWhiteSpace(suggestedOwnedQuery)
                ? $"manage-aliases:{focusMissingRef}"
                : $"manage-aliases:{focusMissingRef}:{suggestedOwnedQuery}");
    public void OpenConfirmDelete(IReadOnlyList<ConfirmItem> items, int reverseDepCount = 0) => Opened.Add($"confirm:{items.Count}");
    public void OpenFix(long varFileId, string? codepage) => Opened.Add($"fix:{codepage}");
    public void OpenDupeReview(DuplicateGroup group) => Opened.Add($"dupe:{group.IdentityKey}");
    public void OpenPresetEdit(long presetId, string name) => Opened.Add($"preset:{presetId}");
    public void OpenEditMeta(long packageId, Action? onSaved = null) => Opened.Add($"edit-meta:{packageId}");
}

// Shared, reusable public test doubles for the gap screen/tab units (18-gap G-C/G-D).
// Each returns empty/default data unless a test overrides via the provided seams.

public sealed class StubTiering(
    TierClassCounts? counts = null,
    IReadOnlyList<MisplacedItem>? misplaced = null,
    TierMigrationPlan? plan = null) : ITieringService
{
    public Task<TierClassCounts> ClassCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(counts ?? new TierClassCounts(0, 0, 0));
    public Task<PageResult<MisplacedItem>> MisplacedPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<MisplacedItem>(misplaced ?? [], (misplaced ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken ct = default) =>
        Task.FromResult(misplaced ?? []);
    public Task<TierMigrationPlan> BuildPlanAsync(CancellationToken ct = default) =>
        Task.FromResult(plan ?? new TierMigrationPlan([], 0));
    public Task<TierPolicy> PolicyAsync(CancellationToken ct = default) =>
        Task.FromResult(new TierPolicy([]));
    public Task<PageResult<StaleVersion>> StaleVersionsPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<StaleVersion>([], 0, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<StaleVersion>> StaleVersionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StaleVersion>>([]);
}

public sealed class StubReclaim(IReadOnlyList<DuplicateGroup>? groups = null) : IReclaimService
{
    public Task<PageResult<DuplicateGroup>> ExactGroupsPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<DuplicateGroup>(groups ?? [], (groups ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(groups ?? []);
    public Task<PageResult<NearDuplicateGroup>> NearDuplicateGroupsPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<NearDuplicateGroup>([], 0, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<NearDuplicateGroup>> NearDuplicateGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NearDuplicateGroup>>([]);
    public Task<ReclaimResult> TrashRedundantAsync(long keep, IReadOnlyList<long> trash, CancellationToken ct = default) =>
        Task.FromResult(new ReclaimResult(trash.Count, 0));
}

public sealed class StubHealth(IReadOnlyList<EncodingGroup>? groups = null) : IHealthService
{
    public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(groups ?? []);
    public Task<PageResult<IntegrityIssue>> IntegrityPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<IntegrityIssue>([], 0, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
    public Task<PageResult<IntegrityIssue>> MissingMetaPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<IntegrityIssue>([], 0, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
    public Task<Result<long>> FixAsync(long varFileId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(varFileId));
    public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken ct = default) =>
        Task.FromResult(new BulkActionResult(0, 0));
    public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, IProgressSink progress, CancellationToken ct = default) =>
        FixGroupAsync(codepageFilter, ct);
    public Task<BulkActionResult> FixManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default) =>
        Task.FromResult(new BulkActionResult(varFileIds.Count, 0));
}

public sealed class StubProposals(IReadOnlyList<Proposal>? pending = null) : IProposalService
{
    public List<Proposal> Approved { get; } = [];
    public List<Proposal> Rejected { get; } = [];
    public Task<PageResult<Proposal>> ListPageAsync(ProposalKind? kind, PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<Proposal>((pending ?? []).Where(p => kind is null || p.Kind == kind.Value).ToList(), (pending ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<Proposal>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(pending ?? []);
    public Task<ProposalActionResult> ApproveAsync(Proposal p, CancellationToken ct = default)
    { Approved.Add(p); return Task.FromResult(new ProposalActionResult(true, "ok")); }
    public Task<ProposalActionResult> RejectAsync(Proposal p, CancellationToken ct = default)
    { Rejected.Add(p); return Task.FromResult(new ProposalActionResult(true, "ok")); }
}

public sealed class StubTrash(
    IReadOnlyList<TrashItemDto>? items = null,
    IReadOnlyList<BackupDto>? backups = null) : ITrashQueryService
{
    public List<string> Restored { get; } = [];
    public List<string> Purged { get; } = [];
    public bool BackedUp { get; private set; }
    public Task<PageResult<TrashItemDto>> ListPageAsync(PageRequest request, string? searchText = null, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<TrashItemDto>(items ?? [], (items ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(items ?? []);
    public Task<Result> RestoreAsync(string id, CancellationToken ct = default)
    { Restored.Add(id); return Task.FromResult(Result.Success()); }
    public Task<Result> PurgeAsync(string id, CancellationToken ct = default)
    { Purged.Add(id); return Task.FromResult(Result.Success()); }
    public Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken ct = default) =>
        Task.FromResult(backups ?? []);
    public Task<Result<BackupDto>> BackupNowAsync(CancellationToken ct = default)
    { BackedUp = true; return Task.FromResult(Result.Success(new BackupDto("catalog.bak", default, 0))); }
}

public sealed class StubReposEmpty : VarVault.Sdk.Repositories.IRepositoryService
{
    public Task<Result<VarVault.Sdk.Repositories.RepositoryInfo>> RegisterAsync(VarVault.Sdk.Repositories.RegisterRepositoryRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VarVault.Sdk.Repositories.RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VarVault.Sdk.Repositories.RepositoryInfo>>([]);
    public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> RenameAsync(Guid id, string name, CancellationToken ct = default) => Task.FromResult(true);
    public Task<VarVault.Sdk.Repositories.RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => Task.FromResult<VarVault.Sdk.Repositories.RepositoryInfo?>(null);
    public Task<Result<VarVault.Sdk.Repositories.RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<VarVault.Sdk.Repositories.RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => Task.FromResult<VarVault.Sdk.Repositories.RepositoryInfo?>(null);
    public Task<VarVault.Sdk.Repositories.RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => Task.FromResult<VarVault.Sdk.Repositories.RepositoryInfo?>(null);
}

public sealed class StubMissing(IReadOnlyList<MissingDependency>? items = null) : IMissingDepsQuery
{
    public Task<PageResult<MissingDependency>> GetPageAsync(PageRequest request, string? searchText = null, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<MissingDependency>(items ?? [], (items ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken ct = default) => Task.FromResult(items ?? []);
}

public sealed class StubDashboard(DashboardSummary? summary = null) : IDashboardService
{
    public Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default) =>
        Task.FromResult(summary ?? new DashboardSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, []));
}

public sealed class StubLibraryQuery(LibraryPage? page = null) : ILibraryQueryService
{
    public Task<LibraryPage> GetPageAsync(LibraryQuery q, CancellationToken ct = default) => Task.FromResult(page ?? new LibraryPage([], 0));
    public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery q, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<long>>([]);
}

public sealed class StubPresetsMin : VarVault.Sdk.Presets.IPresetService
{
    private long _nextId = 100;
    public Task<Result<VarVault.Sdk.Presets.PresetInfo>> CreateAsync(string name, IEnumerable<string> refs, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new VarVault.Sdk.Presets.PresetInfo(_nextId++, name, refs.Count())));
    public Task<IReadOnlyList<VarVault.Sdk.Presets.PresetInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VarVault.Sdk.Presets.PresetInfo>>([]);
    public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
    public Task<Result<VarVault.Sdk.Presets.PresetInfo>> AddMemberAsync(long id, string r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<VarVault.Sdk.Presets.PresetInfo>> RemoveMemberAsync(long id, string r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PageResult<string>> MembersPageAsync(long id, PageRequest request, CancellationToken ct = default) => Task.FromResult(new PageResult<string>([], 0, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<string>> MembersAsync(long id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<VarVault.Sdk.Presets.ActivationPreview?> PreviewActivationAsync(long id, CancellationToken ct = default) => Task.FromResult<VarVault.Sdk.Presets.ActivationPreview?>(null);
    public Task RefreshMemberResolutionsAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class StubAnalytics(
    IReadOnlyList<SpaceByGroup>? byCreator = null,
    IReadOnlyList<SpaceByGroup>? byType = null,
    IReadOnlyList<SpaceByGroup>? byTier = null) : IAnalyticsService
{
    public Task<PageResult<SpaceByGroup>> SpaceByCreatorPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<SpaceByGroup>(byCreator ?? [], (byCreator ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken ct = default) => Task.FromResult(byCreator ?? []);
    public Task<IReadOnlyList<SpaceByGroup>> SpaceByTypeAsync(CancellationToken ct = default) => Task.FromResult(byType ?? []);
    public Task<IReadOnlyList<SpaceByGroup>> SpaceByTierAsync(CancellationToken ct = default) => Task.FromResult(byTier ?? []);
}

public sealed class StubActivityLog(IReadOnlyList<VarVault.Sdk.Activation.ActivityRecord>? records = null)
    : VarVault.Sdk.Activation.IActivityLog
{
    public Task RecordAsync(string kind, string desc, long? pkg = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<PageResult<VarVault.Sdk.Activation.ActivityRecord>> GetRecentPageAsync(PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PageResult<VarVault.Sdk.Activation.ActivityRecord>(records ?? [], (records ?? []).Count, request.SafePageNumber, request.SafePageSize));
    public Task<IReadOnlyList<VarVault.Sdk.Activation.ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken ct = default) =>
        Task.FromResult(records ?? []);
}

public sealed class StubSettings : ISettingsService
{
    private readonly Dictionary<string, string> _store = new();
    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
    public Task SetAsync(string key, string value, CancellationToken ct = default) { _store[key] = value; return Task.CompletedTask; }
    public Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v == "true" : fallback);
    public Task SetBoolAsync(string key, bool value, CancellationToken ct = default) { _store[key] = value ? "true" : "false"; return Task.CompletedTask; }
    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(_store);
}
