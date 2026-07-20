using System.IO;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class RepositoryEnumeratorTests
{
    private readonly RepositoryEnumerator _sut = new();

    [Fact]
    public void Finds_live_vars_excludes_link_dirs_tags_quarantine()
    {
        using var dir = new TempDirectory();
        Touch(dir, "live/Creator.Package.1.var");
        Touch(dir, "___VarRedundant____old/Creator.Package.2.var");
        Touch(dir, "___VarsLink___/should_be_ignored.var"); // link farm — excluded
        Touch(dir, "notes.txt");                             // not a var

        var found = _sut.Enumerate(dir.Path).ToList();

        var live = Assert.Single(found, v => v.RelativePath.Contains("Creator.Package.1", StringComparison.Ordinal));
        Assert.Equal(QuarantineKind.None, live.Quarantine);
        Assert.True(live.IsLive);

        var quarantined = Assert.Single(found, v => v.RelativePath.Contains("Creator.Package.2", StringComparison.Ordinal));
        Assert.Equal(QuarantineKind.Redundant, quarantined.Quarantine);

        Assert.DoesNotContain(found, v => v.RelativePath.Contains("should_be_ignored", StringComparison.Ordinal));
        Assert.DoesNotContain(found, v => v.RelativePath.EndsWith("notes.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Include_quarantined_false_omits_quarantined_vars()
    {
        using var dir = new TempDirectory();
        Touch(dir, "live/A.B.1.var");
        Touch(dir, "___StaleVars___/A.B.2.var");

        var found = _sut.Enumerate(dir.Path, includeQuarantined: false).ToList();

        Assert.Single(found);
        Assert.All(found, v => Assert.Equal(QuarantineKind.None, v.Quarantine));
    }

    [Fact]
    public void Captures_size_and_mtime_without_opening_files()
    {
        using var dir = new TempDirectory();
        var path = Touch(dir, "live/A.B.1.var", "hello world payload");

        var found = _sut.Enumerate(dir.Path).Single();
        Assert.Equal(new FileInfo(path).Length, found.SizeBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(path), found.FileMtimeUtc);
    }

    [Fact]
    public void Skips_reparse_point_directories()
    {
        using var dir = new TempDirectory();
        Touch(dir, "real/A.B.1.var");
        var linkPath = Path.Combine(dir.Path, "linkdir");

        try
        {
            Directory.CreateSymbolicLink(linkPath, Path.Combine(dir.Path, "real"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // symlink creation needs Developer Mode/admin — skip where unavailable
        }

        var found = _sut.Enumerate(dir.Path).ToList();
        // The real var is found once via "real/", never a second time through the junctioned "linkdir/".
        Assert.Single(found);
        Assert.Contains("real", found[0].RelativePath, StringComparison.Ordinal);
    }

    // D: real-corpus probe — quarantine tagging and live counts on the actual repo.
    [SkippableFact]
    public void Real_repo_tags_quarantine_and_finds_live_vars()
    {
        Skip.If(TestCorpus.Primary is null, "requires VARVAULT_TEST_CORPUS");
        var repo = TestCorpus.Primary!;

        var all = _sut.Enumerate(repo).ToList();
        Assert.NotEmpty(all);

        var live = all.Where(v => v.IsLive).ToList();
        var quarantined = all.Where(v => v.Quarantine == QuarantineKind.Redundant).ToList();

        Assert.NotEmpty(live);        // Vam_Installer_notSameContentFiles1 vars
        Assert.NotEmpty(quarantined); // ___VarRedundant____* vars tagged, not live
    }

    private static string Touch(TempDirectory dir, string relativePath, string content = "x")
    {
        var full = Path.Combine(dir.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }
}
