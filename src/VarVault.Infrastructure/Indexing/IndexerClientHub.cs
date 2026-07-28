using VarVault.Common;
using VarVault.Sdk.Indexer;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// DI-facing <see cref="IIndexerClient"/> that can be redirected at startup from the in-process
/// fallback to the live named-pipe worker. Import / Onboarding resolve this via DI; the GUI also
/// sets <see cref="Redirect"/> so every writer path hits the same catalog owner. (A12.)
/// </summary>
public sealed class IndexerClientHub(IIndexerClient fallback) : IIndexerClient
{
    private volatile IIndexerClient _inner = fallback;

    /// <summary>Swap the target client (typically the out-of-process named-pipe worker).</summary>
    public void Redirect(IIndexerClient client)
    {
        Guard.NotNull(client);
        _inner = client;
    }

    /// <summary>Current target (pipe or in-proc). Useful for UI that wants the live instance.</summary>
    public IIndexerClient Current => _inner;

    public Task<Result<IndexerStatus>> PingAsync(CancellationToken cancellationToken = default) =>
        _inner.PingAsync(cancellationToken);

    public Task<Result<Guid>> StartIndexAllAsync(bool forceFull = false, CancellationToken cancellationToken = default) =>
        _inner.StartIndexAllAsync(forceFull, cancellationToken);

    public Task<Result<Guid>> StartIndexRepositoryAsync(
        Guid repositoryId,
        bool forceFull = false,
        CancellationToken cancellationToken = default) =>
        _inner.StartIndexRepositoryAsync(repositoryId, forceFull, cancellationToken);

    public Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _inner.CancelAsync(jobId, cancellationToken);

    public Task<Result<IndexerStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<Result> RegisterOwnerAsync(CancellationToken cancellationToken = default) =>
        _inner.RegisterOwnerAsync(cancellationToken);

    public Task<Result> UnregisterOwnerAsync(CancellationToken cancellationToken = default) =>
        _inner.UnregisterOwnerAsync(cancellationToken);

    public Task<Result> ExtractContentPreviewAsync(long contentItemId, CancellationToken cancellationToken = default) =>
        _inner.ExtractContentPreviewAsync(contentItemId, cancellationToken);

    public Task<Result> ExtractContentFocusPreviewAsync(long contentItemId, CancellationToken cancellationToken = default) =>
        _inner.ExtractContentFocusPreviewAsync(contentItemId, cancellationToken);
}
