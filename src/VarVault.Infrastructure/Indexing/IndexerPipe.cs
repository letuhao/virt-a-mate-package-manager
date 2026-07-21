using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VarVault.Common;
using VarVault.Sdk.Indexer;

namespace VarVault.Infrastructure.Indexing;

/// <summary>JSON-line named-pipe transport for indexer commands. (A12.)</summary>
public static class IndexerPipeProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
            return default;
        return JsonSerializer.Deserialize<T>(line, JsonOptions);
    }
}

/// <summary>Named-pipe client used by the GUI when the indexer runs out-of-process.</summary>
public sealed class NamedPipeIndexerClient(string pipeName, int ownerProcessId = 0) : IIndexerClient
{
    /// <summary>Default connect wait for real commands (start/status/cancel).</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Connect wait for liveness probes. Must stay short: startup used to call Ping on the UI thread
    /// with a 5s timeout × many retries and looked like the app would never open.
    /// </summary>
    public static readonly TimeSpan LivenessConnectTimeout = TimeSpan.FromMilliseconds(250);

    public Task<Result<IndexerStatus>> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new IndexerCommand(IndexerCommandKind.Ping), DefaultConnectTimeout, cancellationToken);

    /// <summary>Fast liveness probe — fails quickly when no worker is listening.</summary>
    public Task<Result<IndexerStatus>> PingLivenessAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new IndexerCommand(IndexerCommandKind.Ping), LivenessConnectTimeout, cancellationToken);

    public async Task<Result<Guid>> StartIndexAllAsync(bool forceFull = false, CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            new IndexerCommand(IndexerCommandKind.StartIndexAll, ForceFull: forceFull),
            DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        if (status.IsFailure)
            return Result.Failure<Guid>(status.Error);
        return status.Value.JobId is { } id
            ? Result.Success(id)
            : Result.Failure<Guid>("indexer.start", status.Value.Error ?? "failed to start");
    }

    public async Task<Result<Guid>> StartIndexRepositoryAsync(Guid repositoryId, bool forceFull = false, CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            new IndexerCommand(IndexerCommandKind.StartIndexRepository, RepositoryId: repositoryId, ForceFull: forceFull),
            DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        if (status.IsFailure)
            return Result.Failure<Guid>(status.Error);
        return status.Value.JobId is { } id
            ? Result.Success(id)
            : Result.Failure<Guid>("indexer.start", status.Value.Error ?? "failed to start");
    }

    public async Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(new IndexerCommand(IndexerCommandKind.CancelJob, JobId: jobId), DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        return status.IsSuccess ? Result.Success() : Result.Failure(status.Error);
    }

    public Task<Result<IndexerStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new IndexerCommand(IndexerCommandKind.GetStatus), DefaultConnectTimeout, cancellationToken);

    public async Task<Result> RegisterOwnerAsync(CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(new IndexerCommand(IndexerCommandKind.RegisterOwner), DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        return status.IsSuccess ? Result.Success() : Result.Failure(status.Error);
    }

    public async Task<Result> UnregisterOwnerAsync(CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(new IndexerCommand(IndexerCommandKind.UnregisterOwner), DefaultConnectTimeout, cancellationToken).ConfigureAwait(false);
        return status.IsSuccess ? Result.Success() : Result.Failure(status.Error);
    }

    public async Task<Result> ExtractContentPreviewAsync(long contentItemId, CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            new IndexerCommand(IndexerCommandKind.ExtractContentPreview, ContentItemId: contentItemId),
            DefaultConnectTimeout,
            cancellationToken).ConfigureAwait(false);
        if (status.IsFailure)
            return Result.Failure(status.Error);
        return status.Value.Error is null
            ? Result.Success()
            : Result.Failure("indexer.preview", status.Value.Error);
    }

    private async Task<Result<IndexerStatus>> SendAsync(
        IndexerCommand command, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        // Stamp the owner pid on every command so the worker's liveness watchdog sees an attached GUI.
        if (ownerProcessId > 0 && command.OwnerProcessId is null)
            command = command with { OwnerProcessId = ownerProcessId };
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(connectTimeout, cancellationToken).ConfigureAwait(false);
            await IndexerPipeProtocol.WriteAsync(pipe, command, cancellationToken).ConfigureAwait(false);
            var status = await IndexerPipeProtocol.ReadAsync<IndexerStatus>(pipe, cancellationToken).ConfigureAwait(false);
            return status is null
                ? Result.Failure<IndexerStatus>("indexer.pipe", "empty response")
                : Result.Success(status);
        }
        catch (Exception ex)
        {
            return Result.Failure<IndexerStatus>("indexer.pipe", ex.Message);
        }
    }
}

/// <summary>Named-pipe server loop hosted by <c>VarVault.Indexer</c>.</summary>
public sealed class NamedPipeIndexerServer(string pipeName, IIndexerWorker worker)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var command = await IndexerPipeProtocol.ReadAsync<IndexerCommand>(pipe, cancellationToken).ConfigureAwait(false);
                if (command is null)
                    continue;
                var status = await worker.HandleAsync(command, cancellationToken).ConfigureAwait(false);
                await IndexerPipeProtocol.WriteAsync(pipe, status, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Drop bad connections; keep serving.
            }
        }
    }
}
