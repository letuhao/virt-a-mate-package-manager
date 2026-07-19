using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N3 · Migration facade: persists each requested move as a <see cref="MigrationJob"/> and runs it
/// through <see cref="MigrationRunner"/> (copy → verify → rename → trash source). (16-checklist BE-N3.)
/// </summary>
public sealed class EfMigrationService(VarVaultDbContext db, MigrationRunner runner) : IMigrationService
{
    public async Task<MigrationRunResult> RunAsync(IReadOnlyList<MigrationRequest> moves, CancellationToken cancellationToken = default)
    {
        int moved = 0, failed = 0;
        foreach (var move in moves)
        {
            var varFile = await db.VarFiles.FirstOrDefaultAsync(v => v.Id == move.VarFileId, cancellationToken).ConfigureAwait(false);
            if (varFile is null) { failed++; continue; }

            var job = new MigrationJob
            {
                VarFileId = varFile.Id,
                SourceRepositoryId = varFile.RepositoryId,
                TargetRepositoryId = move.TargetRepositoryId,
                State = MigrationState.Planned,
            };
            db.MigrationJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var state = await runner.RunAsync(job.Id, cancellationToken).ConfigureAwait(false);
            if (state == MigrationState.Done) moved++; else failed++;
        }
        return new MigrationRunResult(moved, failed);
    }
}
