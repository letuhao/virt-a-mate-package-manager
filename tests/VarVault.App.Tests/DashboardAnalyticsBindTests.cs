using System.Collections.Generic;
using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-4 — Dashboard tier bars bound raw bytes into a clamped 0..1 meter (always full), and Analytics
/// "wasting fast storage" was never computed (always 0). (audit 25 §5.3.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DashboardAnalyticsBindTests
{
    // G-4.1 · the tier bar binds used/capacity, not raw bytes.
    [Fact]
    public void Tier_used_fraction_is_used_over_capacity()
    {
        Assert.Equal(0.5, new TierUtilization(1, 500, 1000, 1).UsedFraction, 3);
        Assert.Equal(0.0, new TierUtilization(2, 100, 0, 0).UsedFraction); // no capacity → 0, never divide-by-zero
        Assert.Equal(1.0, new TierUtilization(3, 5000, 1000, 1).UsedFraction); // clamped to 1
    }

    // G-4.2 · "wasting fast storage" sums items sitting on a faster tier than their class wants.
    [Fact]
    public async Task Wasted_on_fast_sums_items_on_a_faster_tier_than_desired()
    {
        var misplaced = new List<MisplacedItem>
        {
            new(1, "A.1", "Cold", 1, 3, 100, "cold on SSD"),   // T1 < T3 → wasted fast
            new(2, "B.1", "Cold", 2, 3, 50, "cold on SSD"),    // T2 < T3 → wasted fast
            new(3, "C.1", "Hot", 3, 1, 999, "hot on HDD"),     // T3 > T1 → NOT wasted-fast (it's too slow)
        };
        var vm = new AnalyticsViewModel(new StubAnalytics(), new StubTiering(misplaced: misplaced));

        await vm.RefreshAsync();

        Assert.Equal(150, vm.WastedOnFastBytes); // 100 + 50; the hot-on-HDD item is excluded
    }
}
