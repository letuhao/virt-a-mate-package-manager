using VarVault.Common.Formatting;

namespace VarVault.Common.Tests;

/// <summary>Unit coverage for the shared byte humanizer that replaced the ad-hoc per-VM formatters and the
/// raw-byte bindings on Dashboard / Duplicates / Library. (28-checklist A1.1.)</summary>
public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(64L * 1024, "64 KB")]
    [InlineData(1L << 20, "1 MB")]
    [InlineData(812L * (1 << 20), "812 MB")]
    [InlineData(1L << 30, "1.0 GB")]
    [InlineData(1_500_000_000, "1.4 GB")]
    [InlineData(1L << 40, "1.00 TB")]
    [InlineData(2_000_000_000_000, "1.82 TB")]
    public void Humanize_formats_with_largest_fitting_unit(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Humanize(bytes));

    [Fact]
    public void Humanize_never_returns_a_bare_digit_string_for_large_values()
    {
        // The whole point of the fix: no more "4101826139" with no unit.
        var s = ByteSize.Humanize(4_101_826_139);
        Assert.Contains("GB", s);
        Assert.DoesNotContain("bytes", s);
    }
}
