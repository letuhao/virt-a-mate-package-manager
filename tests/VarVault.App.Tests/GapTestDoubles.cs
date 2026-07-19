using System.Collections.Generic;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;

namespace VarVault.App.Tests;

// Shared, reusable public test doubles for the gap screen/tab units (18-gap G-C/G-D).
// Each returns empty/default data unless a test overrides via the provided seams.

public sealed class StubTiering(
    TierClassCounts? counts = null,
    IReadOnlyList<MisplacedItem>? misplaced = null,
    TierMigrationPlan? plan = null) : ITieringService
{
    public Task<TierClassCounts> ClassCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(counts ?? new TierClassCounts(0, 0, 0));
    public Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken ct = default) =>
        Task.FromResult(misplaced ?? []);
    public Task<TierMigrationPlan> BuildPlanAsync(CancellationToken ct = default) =>
        Task.FromResult(plan ?? new TierMigrationPlan([], 0));
}

public sealed class StubReclaim(IReadOnlyList<DuplicateGroup>? groups = null) : IReclaimService
{
    public Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(groups ?? []);
    public Task<ReclaimResult> TrashRedundantAsync(long keep, IReadOnlyList<long> trash, CancellationToken ct = default) =>
        Task.FromResult(new ReclaimResult(trash.Count, 0));
}

public sealed class StubHealth(IReadOnlyList<EncodingGroup>? groups = null) : IHealthService
{
    public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(groups ?? []);
    public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
    public Task<Result<long>> FixAsync(long varFileId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(varFileId));
}

public sealed class StubProposals(IReadOnlyList<Proposal>? pending = null) : IProposalService
{
    public List<Proposal> Approved { get; } = [];
    public List<Proposal> Rejected { get; } = [];
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
