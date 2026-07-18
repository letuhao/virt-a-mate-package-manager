using System.Reflection;
using NetArchTest.Rules;
using VarVault.Common;
using VarVault.Domain.ValueObjects;
using VarVault.Host;
using VarVault.Infrastructure;
using VarVault.Modules.Indexing;
using VarVault.Modules.Repositories;
using VarVault.Sdk.Modularity;

namespace VarVault.Architecture.Tests;

/// <summary>Enforces the dependency direction from docs/new-app/11-Architecture-and-Modularity.md.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Common = typeof(Result).Assembly;
    private static readonly Assembly Sdk = typeof(IModule).Assembly;
    private static readonly Assembly Domain = typeof(PackageId).Assembly;
    private static readonly Assembly Repositories = typeof(RepositoriesModule).Assembly;
    private static readonly Assembly Indexing = typeof(IndexingModule).Assembly;

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on [{string.Join(", ", forbidden)}]. " +
            $"Offenders: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Common_depends_on_nothing_in_VarVault() =>
        AssertNoDependency(Common,
            "VarVault.Sdk", "VarVault.Domain", "VarVault.Infrastructure",
            "VarVault.Modules", "VarVault.Host");

    [Fact]
    public void Sdk_depends_only_on_Common() =>
        AssertNoDependency(Sdk,
            "VarVault.Domain", "VarVault.Infrastructure", "VarVault.Modules", "VarVault.Host");

    [Fact]
    public void Domain_depends_only_on_Common() =>
        AssertNoDependency(Domain,
            "VarVault.Sdk", "VarVault.Infrastructure", "VarVault.Modules", "VarVault.Host");

    [Fact]
    public void Modules_do_not_reference_each_other_or_infrastructure_or_host()
    {
        AssertNoDependency(Repositories,
            "VarVault.Modules.Indexing", "VarVault.Infrastructure", "VarVault.Host");
        AssertNoDependency(Indexing,
            "VarVault.Modules.Repositories", "VarVault.Infrastructure", "VarVault.Host");
    }
}
