using VarVault.Common;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;

namespace VarVault.Sdk.Library;

/// <summary>Result of adding + indexing a repository through onboarding.</summary>
public sealed record OnboardingResult(RepositoryInfo Repository, IndexRunSummary Index);

/// <summary>
/// BE-N13 · Onboarding / add-repo: register a folder (media/benchmark/tier detected), set its reserve, and
/// index it (which resolves deps + recomputes usage). (16-checklist BE-N13.)
/// </summary>
public interface IOnboardingService
{
    Task<Result<OnboardingResult>> AddAndIndexAsync(string name, string path, long? reserveBytes = null, CancellationToken cancellationToken = default);
}
