using System.IO;
using System.Security.Cryptography;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Durable move: copy→flush→verify→atomic rename, leaving the source intact for the caller to delete
/// last. A verified destination or nothing — never a half-written file. (5.8/5.13.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class DurableFileMoverTests
{
    private readonly DurableFileMover _sut = new();

    [Fact]
    public async Task Copies_verifies_and_renames_leaving_the_source_intact()
    {
        using var dir = new TempDirectory();
        var source = dir.File("source.var");
        var dest = Path.Combine(dir.Path, "tier1", "dest.var");
        var payload = RandomBytes(200_000);
        await File.WriteAllBytesAsync(source, payload);

        var result = await _sut.CopyVerifyRenameAsync(source, dest);

        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.True(File.Exists(dest));
        Assert.True(File.Exists(source)); // source preserved — caller deletes it last (5.12)
        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        Assert.Equal(Sha(payload), result.Value.VerifiedHash);
        Assert.False(File.Exists(dest + ".partial")); // no leftover temp
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_existing_destination()
    {
        using var dir = new TempDirectory();
        var source = dir.File("s.var");
        var dest = dir.File("d.var");
        await File.WriteAllTextAsync(source, "a");
        await File.WriteAllTextAsync(dest, "existing");

        var result = await _sut.CopyVerifyRenameAsync(source, dest);
        Assert.True(result.IsFailure);
        Assert.Equal("move.exists", result.Error.Code);
        Assert.Equal("existing", await File.ReadAllTextAsync(dest)); // untouched
    }

    [Fact]
    public async Task Missing_source_is_a_failure()
    {
        using var dir = new TempDirectory();
        var result = await _sut.CopyVerifyRenameAsync(Path.Combine(dir.Path, "nope.var"), dir.File("d.var"));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Cancellation_leaves_no_destination()
    {
        using var dir = new TempDirectory();
        var source = dir.File("s.var");
        var dest = dir.File("d.var");
        await File.WriteAllBytesAsync(source, RandomBytes(500_000));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CopyVerifyRenameAsync(source, dest, cts.Token));

        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".partial"));
        Assert.True(File.Exists(source)); // source never at risk
    }

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        // Deterministic-ish fill without Random (available in tests, but keep it simple/repeatable).
        for (var i = 0; i < n; i++)
            b[i] = (byte)(i * 31 + 7);
        return b;
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
