using System.IO;
using VarVault.App.Composition;
using VarVault.TestKit;
using Xunit;

namespace VarVault.App.Tests;

/// <summary>
/// 24-checklist C1 · A startup composition failure yields a copyable error view-model, never a null shell that
/// App.axaml.cs would render as a blank window.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class StartupErrorTests
{
    [Fact]
    public void Composition_failure_returns_error_vm_not_null_shell()
    {
        // A file where a directory is expected → Directory.CreateDirectory throws → the error path runs.
        using var tmp = new TempDirectory();
        var notADir = Path.Combine(tmp.Path, "not-a-dir");
        File.WriteAllText(notADir, "x");

        var (shell, error) = AppHost.TryCreateShellOrError(notADir);

        Assert.Null(shell);
        Assert.NotNull(error);
        Assert.Contains("couldn't start", error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(error.Detail));
    }
}
