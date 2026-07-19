using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N13 · Onboarding. Registers the folder (media/tier detected by <see cref="IRepositoryService"/>),
/// applies the reserve, then indexes it through the orchestrator (resolve + usage). (16-checklist BE-N13.)
/// </summary>
public sealed class EfOnboardingService(
    VarVaultDbContext db,
    IRepositoryService repositories,
    IIndexOrchestrator orchestrator) : IOnboardingService
{
    public async Task<Result<OnboardingResult>> AddAndIndexAsync(string name, string path, long? reserveBytes = null, CancellationToken cancellationToken = default)
    {
        var registered = await repositories.RegisterAsync(new RegisterRepositoryRequest(name, path), cancellationToken).ConfigureAwait(false);
        if (registered.IsFailure)
            return Result.Failure<OnboardingResult>(registered.Error.Code, registered.Error.Message);

        if (reserveBytes is { } reserve)
        {
            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == registered.Value.Id, cancellationToken).ConfigureAwait(false);
            if (repo is not null)
            {
                repo.MinFreeBytes = reserve;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var index = await orchestrator.IndexRepositoryAsync(registered.Value.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(new OnboardingResult(registered.Value, index));
    }
}
