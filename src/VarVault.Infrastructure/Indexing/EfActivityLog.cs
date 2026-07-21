using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Paging;

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

    public async Task<PageResult<ActivityRecord>> GetRecentPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var query = db.ActivityEntries.AsNoTracking();
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(e => e.TimestampUnixMs)
            .ThenByDescending(e => e.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(e => new { e.Kind, e.Description, e.TimestampUnixMs })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PageResult<ActivityRecord>(
            rows.Select(r => new ActivityRecord(r.Kind, r.Description, DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampUnixMs).UtcDateTime)).ToList(),
            total,
            page.SafePageNumber,
            page.SafePageSize);
    }

    public async Task<IReadOnlyList<ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return (await GetRecentPageAsync(new PageRequest(1, Math.Clamp(limit, 1, 100)), cancellationToken).ConfigureAwait(false)).Items;
    }
}
