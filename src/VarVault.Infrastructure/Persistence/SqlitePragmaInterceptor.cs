using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Re-applies baseline SQLite pragmas on every opened connection so pooled connections
/// do not lose WAL/FK/busy_timeout. (Production gap fix — CatalogDatabaseInitializer alone is insufficient.)
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SqlitePragmas.ApplyBaseline(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        SqlitePragmas.ApplyBaseline(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
