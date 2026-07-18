using System.IO;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Repositories;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Drive profiling against a real fixed volume (the test temp dir). Media detection is best-effort
/// (Win32); an unknown result falls back to HDD, so we assert the profile is sane, not exact
/// hardware. (BE-R2/R3/R4.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class DriveProfilerTests
{
    private readonly DriveProfiler _sut = new();

    [Fact]
    public void Profiles_a_fixed_volume_with_capacity_and_media_type()
    {
        using var dir = new TempDirectory();
        var profile = _sut.Profile(dir.Path);

        // A local temp dir is on a fixed drive → not removable/network.
        Assert.NotEqual(MediaType.Removable, profile.MediaType);
        Assert.NotEqual(MediaType.Network, profile.MediaType);
        Assert.Contains(profile.MediaType, new[] { MediaType.Nvme, MediaType.Ssd, MediaType.Hdd });

        Assert.NotNull(profile.CapacityBytes);
        Assert.True(profile.CapacityBytes > 0);
        Assert.NotNull(profile.FreeBytes);
    }

    [Fact]
    public void Volume_serial_is_read_on_windows()
    {
        using var dir = new TempDirectory();
        var profile = _sut.Profile(dir.Path);
        if (OperatingSystem.IsWindows())
            Assert.False(string.IsNullOrEmpty(profile.VolumeSerial));
    }

    [Fact]
    public void Capacity_refresh_matches_drive_info()
    {
        using var dir = new TempDirectory();
        var (capacity, free) = _sut.GetCapacity(dir.Path);
        var info = new DriveInfo(Path.GetPathRoot(dir.Path)!);
        Assert.Equal(info.TotalSize, capacity);
        // Free space can drift between the two reads; just assert it's populated and plausible.
        Assert.NotNull(free);
        Assert.True(free <= capacity);
    }
}
