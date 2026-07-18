using VarVault.Common;

namespace VarVault.TestKit;

/// <summary>Controllable clock for deterministic time in tests.</summary>
public sealed class FakeClock(DateTimeOffset? start = null) : IClock
{
    private DateTimeOffset _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
    public void Set(DateTimeOffset to) => _now = to;
}

/// <summary>Self-deleting temp directory for filesystem tests.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "varvault-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Test trait categories: <c>[Trait("Category", TestCategories.Unit)]</c>.</summary>
public static class TestCategories
{
    public const string Unit = "Unit";
    public const string Integration = "Integration";
    public const string E2E = "E2E";
}

/// <summary>Object mother for domain value objects.</summary>
public static class PackageIds
{
    public static Domain.ValueObjects.PackageId Valid(string creator = "Creator", string package = "Package", string version = "1") =>
        Domain.ValueObjects.PackageId.TryParse($"{creator}.{package}.{version}").Value;
}
