using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VarVault.Common;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Startup preparation for the catalog DB: location guard → apply migrations → baseline pragmas →
/// integrity check. Every step returns a <see cref="Result"/> so a bad location or a corrupt
/// database is surfaced to the user instead of causing silent data loss. (Checklist 0.9, 0.10.)
/// </summary>
public sealed class CatalogDatabaseInitializer(ILogger<CatalogDatabaseInitializer> logger)
{
    /// <summary>
    /// Prepare the database for use. <paramref name="repositoryPaths"/> guards against locating the
    /// DB inside a repository; <paramref name="driveTypeResolver"/> is a test seam for drive-type.
    /// </summary>
    public async Task<Result> PrepareAsync(
        VarVaultDbContext db,
        string databasePath,
        IEnumerable<string> repositoryPaths,
        Func<string, DriveType>? driveTypeResolver = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(db);
        Guard.NotNullOrWhiteSpace(databasePath);

        var location = CatalogLocationGuard.Validate(databasePath, repositoryPaths, driveTypeResolver);
        if (location.IsFailure)
        {
            logger.LogError("Catalog location rejected: {Error}", location.Error);
            return location;
        }

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var connection = db.Database.GetDbConnection();
        SqlitePragmas.ApplyBaseline(connection);

        var integrity = CatalogIntegrity.Check(connection);
        if (integrity.IsFailure)
        {
            logger.LogError("Catalog integrity check failed: {Error}", integrity.Error);
            return integrity;
        }

        logger.LogInformation("Catalog database ready at {Path}", databasePath);
        return Result.Success();
    }
}
