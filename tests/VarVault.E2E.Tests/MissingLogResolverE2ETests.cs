using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using Xunit.Abstractions;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>
/// The VaM-log repair resolver (QoL): parse a pasted log → resolve refs (incl. <c>.latest</c>) against the indexed
/// library → report in-library vs still-missing → build the activation preset. Deterministic (temp repo).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class MissingLogResolverE2ETests(ITestOutputHelper log)
{
    /// <summary>Real-data run over the user's own repo + a real VaM error log (env-gated). Set
    /// <c>VARVAULT_REAL_IMPORT_TARGET</c> to the repo to resolve against (e.g. D:\VarVault_test_repo).</summary>
    [SkippableFact]
    public async Task Real_vam_log_resolved_against_real_repo()
    {
        var repoPath = Environment.GetEnvironmentVariable("VARVAULT_REAL_IMPORT_TARGET");
        Skip.If(string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath), "set VARVAULT_REAL_IMPORT_TARGET");

        await using var host = TestHost.Create(withPersistence: true);
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("lib", repoPath!));
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        const string vamLog = """
            !> Missing addon package VL_13.Harness_AV.latest that packageVL_13.Bodysuit_RW.1 depends on
            !> Missing addon package VL_13.Harness_Y.latest that packageVL_13.Bodysuit_RW.1 depends on
            !> Missing addon package VL_13.Mask_Cat.latest that packageVL_13.Bodysuit_RW.1 depends on
            !> Missing addon package VL_13.Swim_V2.latest that packageVL_13.Bodysuit_RW.1 depends on
            !> Missing addon package VL_13.Uniform_VB.latest that packageVL_13.Bodysuit_RW.1 depends on
            """;

        using var scope2 = host.Host.Services.CreateScope();
        var resolver = scope2.ServiceProvider.GetRequiredService<IMissingLogResolver>();
        var analysis = await resolver.AnalyzeAsync(vamLog);

        log.WriteLine($"Repo: {repoPath}");
        log.WriteLine($"Parsed {analysis.Parsed} · in library {analysis.InLibrary} · missing {analysis.NotInLibrary}");
        foreach (var e in analysis.Entries)
            log.WriteLine(e.InLibrary
                ? $"  ✓ {e.Ref}  →  {e.ResolvedVarName}  ({e.RepositoryName} T{e.Tier})"
                : $"  ✗ {e.Ref}  — missing, need import");

        // The parser must not mistake the depender (Bodysuit_RW) for a missing item.
        Assert.Equal(5, analysis.Parsed);
        Assert.DoesNotContain(analysis.Entries, e => e.Package.Contains("Bodysuit"));
    }


    private static async Task<TestHost> SeededAsync(TempDirectory repo)
    {
        WriteVar(repo.Path, "Creator.Pack.1.var", "Creator", "Pack", [("Custom/a.vam", "A")]);
        WriteVar(repo.Path, "Creator.Pack.2.var", "Creator", "Pack", [("Custom/b.vam", "B")]);
        WriteVar(repo.Path, "Other.Thing.5.var", "Other", "Thing", [("Custom/c.vam", "C")]);

        var host = TestHost.Create(withPersistence: true);
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("lib", repo.Path));
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        return host;
    }

    [Fact]
    public async Task Analyze_resolves_latest_marks_present_and_missing()
    {
        using var repo = new TempDirectory();
        await using var host = await SeededAsync(repo);

        using var scope = host.Host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMissingLogResolver>();

        var log = """
            !> Missing addon package Creator.Pack.latest
            !> Missing addon package Creator.Pack.1
            Other.Thing.5:/Custom/c.vam
            !> Missing addon package Ghost.Missing.9
            """;
        var analysis = await resolver.AnalyzeAsync(log);

        Assert.Equal(4, analysis.Parsed);
        Assert.Equal(3, analysis.InLibrary);
        Assert.Equal(1, analysis.NotInLibrary);

        // .latest dereferences to the newest version we actually hold (.2, not .1).
        var latest = analysis.Entries.Single(e => e.IsLatest && e.Package == "Pack");
        Assert.True(latest.InLibrary);
        Assert.Equal("Creator.Pack.2", latest.ResolvedVarName);
        Assert.Equal("lib", latest.RepositoryName);

        // Exact ref present.
        Assert.True(analysis.Entries.Single(e => e.Ref == "Creator.Pack.1").InLibrary);
        // Path-embedded ref present.
        Assert.True(analysis.Entries.Single(e => e.Package == "Thing").InLibrary);
        // Genuinely absent → flagged for import.
        Assert.False(analysis.Entries.Single(e => e.Package == "Missing").InLibrary);
    }

    [Fact]
    public async Task Activate_builds_the_repair_preset_from_the_found_set()
    {
        using var repo = new TempDirectory();
        await using var host = await SeededAsync(repo);

        using var scope = host.Host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMissingLogResolver>();

        // Privilege-independent: preset membership is built even if symlink creation needs Developer Mode.
        var result = await resolver.ActivateAsync(["Creator.Pack.2", "Other.Thing.5"]);
        Assert.Equal(2, result.MembersActivated);
    }
}
