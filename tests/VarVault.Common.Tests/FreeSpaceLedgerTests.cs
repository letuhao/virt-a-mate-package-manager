using VarVault.Domain.Analyzer;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class FreeSpaceLedgerTests
{
    [Fact]
    public void Reservation_succeeds_within_free_minus_minfree()
    {
        var ledger = new FreeSpaceLedger();
        var repo = Guid.NewGuid();
        // free 1000, minFree 100 → up to 900 reservable.
        Assert.True(ledger.TryReserve(repo, 500, freeBytes: 1000, minFreeBytes: 100));
        Assert.Equal(500, ledger.Reserved(repo));
    }

    [Fact]
    public void Concurrent_reservations_cannot_overfill_past_minfree()
    {
        var ledger = new FreeSpaceLedger();
        var repo = Guid.NewGuid();

        Assert.True(ledger.TryReserve(repo, 500, 1000, 100));  // 500 reserved
        Assert.True(ledger.TryReserve(repo, 400, 1000, 100));  // 900 reserved (== free - minFree)
        Assert.False(ledger.TryReserve(repo, 1, 1000, 100));   // would breach MinFree
    }

    [Fact]
    public void Release_frees_capacity_for_the_next_job()
    {
        var ledger = new FreeSpaceLedger();
        var repo = Guid.NewGuid();

        Assert.True(ledger.TryReserve(repo, 900, 1000, 100));
        Assert.False(ledger.TryReserve(repo, 100, 1000, 100));

        ledger.Release(repo, 900);
        Assert.Equal(0, ledger.Reserved(repo));
        Assert.True(ledger.TryReserve(repo, 100, 1000, 100));
    }

    [Fact]
    public void Reservations_are_per_repository()
    {
        var ledger = new FreeSpaceLedger();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True(ledger.TryReserve(a, 900, 1000, 100));
        Assert.True(ledger.TryReserve(b, 900, 1000, 100)); // independent budget
    }

    [Fact]
    public void Zero_or_negative_bytes_are_rejected()
    {
        var ledger = new FreeSpaceLedger();
        var repo = Guid.NewGuid();
        Assert.False(ledger.TryReserve(repo, 0, 1000, 100));
        Assert.False(ledger.TryReserve(repo, -1, 1000, 100));
        Assert.Equal(0, ledger.Reserved(repo));
    }
}
