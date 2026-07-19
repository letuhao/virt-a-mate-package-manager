using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;

namespace VarVault.Infrastructure.Indexing;

/// <summary>EF audit log: append an entry, read the most-recent first. (Checklist X.12.)</summary>
public sealed class EfActivityLog(VarVaultDbContext db, IClock clock) : IActivityLog
{
    public async Task RecordAsync(string kind, string description, long? packageId = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(kind);
        Guard.NotNull(description);
        db.ActivityEntries.Add(new ActivityEntry
        {
            TimestampUnixMs = clock.UtcNow.ToUnixTimeMilliseconds(),
            Kind = kind,
            Description = description,
            PackageId = packageId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var rows = await db.ActivityEntries.AsNoTracking()
            .OrderByDescending(e => e.TimestampUnixMs)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(e => new { e.Kind, e.Description, e.TimestampUnixMs })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new ActivityRecord(r.Kind, r.Description, DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampUnixMs).UtcDateTime))
            .ToList();
    }
}
