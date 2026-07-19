using VarVault.Infrastructure.Repositories;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>The drive benchmark returns positive read/write throughput for a real directory. (BE-R1, 1.5.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class DriveBenchmarkTests
{
    [Fact]
    public async Task Measures_positive_read_and_write_throughput()
    {
        using var dir = new TempDirectory();
        var result = await new DriveBenchmark().MeasureAsync(dir.Path);

        Assert.True(result.ReadMBps > 0, $"read {result.ReadMBps}");
        Assert.True(result.WriteMBps > 0, $"write {result.WriteMBps}");
    }

    [Fact]
    public async Task Leaves_no_temp_files_behind()
    {
        using var dir = new TempDirectory();
        await new DriveBenchmark().MeasureAsync(dir.Path);
        Assert.Empty(Directory.GetFiles(dir.Path, ".varvault-bench-*"));
    }
}
