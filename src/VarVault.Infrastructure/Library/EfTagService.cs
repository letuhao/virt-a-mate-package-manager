using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>BE-N12 · Tag CRUD over Tag/PackageTag (folded NameKey unique). (16-checklist BE-N12.)</summary>
public sealed class EfTagService(VarVaultDbContext db) : ITagService
{
    public async Task<Result<TagInfo>> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<TagInfo>("tag.name", "Tag name is required.");
        var key = IdentityFold.Compute(name);
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.NameKey == key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return new TagInfo(existing.Id, existing.Name, existing.PackageTags.Count);

        var tag = new Tag { Name = name, NameKey = key };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new TagInfo(tag.Id, tag.Name, 0);
    }

    public async Task<Result> TagAsync(long packageId, long tagId, CancellationToken cancellationToken = default)
    {
        if (await db.PackageTags.AnyAsync(pt => pt.PackageId == packageId && pt.TagId == tagId, cancellationToken).ConfigureAwait(false))
            return Result.Success();
        db.PackageTags.Add(new PackageTag { PackageId = packageId, TagId = tagId });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UntagAsync(long packageId, long tagId, CancellationToken cancellationToken = default)
    {
        var link = await db.PackageTags.FirstOrDefaultAsync(pt => pt.PackageId == packageId && pt.TagId == tagId, cancellationToken).ConfigureAwait(false);
        if (link is null)
            return Result.Success();
        db.PackageTags.Remove(link);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<TagInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagInfo(t.Id, t.Name, t.PackageTags.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<long>> PackageIdsAsync(long tagId, CancellationToken cancellationToken = default) =>
        await db.PackageTags.Where(pt => pt.TagId == tagId).Select(pt => pt.PackageId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
