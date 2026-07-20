using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dedup;
using VarVault.Domain.Indexing;
using VarVault.Domain.ValueObjects;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// A10 · Download-intake facade over the domain <see cref="IntakeClassifier"/>. Inspects each var in a folder
/// (reusing <see cref="IVarInspector"/> + its stored fingerprints), then classifies it against the catalog's
/// signature facts. Read-only — it classifies, it does not import. (24-checklist A10.)
/// </summary>
public sealed class EfIntakeService(VarVaultDbContext db, IVarInspector inspector) : IIntakeService
{
    public async Task<IReadOnlyList<IntakeItem>> ClassifyFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        // Catalog signature facts, loaded once for the whole folder.
        var catalog = (await db.VarFiles
            .Where(v => v.PackageId != null)
            .Select(v => new { v.Package!.IdentityKey, v.ContentSignature, v.PayloadSignature, v.ContentSignatureNoPath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(v => new VarSignatureFacts(v.IdentityKey, v.ContentSignature, v.PayloadSignature, v.ContentSignatureNoPath))
            .ToList();

        var items = new List<IntakeItem>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.var", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var inspection = inspector.Inspect(path, cancellationToken);
            if (inspection.IsFailure || inspection.Value.Signatures is not { } sig)
            {
                items.Add(new IntakeItem(name, "Unreadable", "Skip"));
                continue;
            }

            // Derive identity the same way the catalog does (parse Creator.Package.Version), so identities line up.
            var parsed = PackageId.TryParse(name);
            var identityKey = parsed.IsSuccess ? parsed.Value.IdentityKey : IntakeClassifier.IdentityKeyFor(name);
            var candidate = new VarSignatureFacts(
                identityKey, sig.ContentSignature, sig.PayloadSignature, sig.ContentSignatureNoPath);
            var cls = IntakeClassifier.Classify(candidate, catalog);
            items.Add(new IntakeItem(name, Label(cls), Suggest(cls)));
        }
        return items;
    }

    private static string Label(IntakeClass c) => c switch
    {
        IntakeClass.ExactDuplicate => "Exact duplicate",
        IntakeClass.SameNameDifferentContent => "Same name, different content",
        IntakeClass.NearDuplicate => "Near-duplicate",
        IntakeClass.EncodingVariant => "Encoding-fixed twin",
        _ => "New",
    };

    private static string Suggest(IntakeClass c) => c switch
    {
        IntakeClass.ExactDuplicate => "Skip — already have it",
        IntakeClass.SameNameDifferentContent => "Review — content conflict",
        IntakeClass.NearDuplicate => "Review",
        IntakeClass.EncodingVariant => "Skip — twin of existing",
        _ => "Import",
    };
}
