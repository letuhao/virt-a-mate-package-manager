using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N14 · Command palette. Filters a built-in nav/action registry by substring and appends package
/// search hits from <see cref="ILibraryQueryService"/>. (16-checklist BE-N14.)
/// </summary>
public sealed class EfCommandPaletteService(ILibraryQueryService library) : ICommandPaletteService
{
    // Navigation targets + actions the palette exposes (client dispatches on Target).
    private static readonly CommandHit[] Registry =
    [
        new("nav", "Dashboard", "dashboard"),
        new("nav", "Library", "library"),
        new("nav", "Loading presets", "presets"),
        new("nav", "Tiering & migration", "tiering"),
        new("nav", "Duplicates & reclaim", "dupes"),
        new("nav", "Analytics", "analytics"),
        new("nav", "Proposals", "proposals"),
        new("nav", "Health & fix", "health"),
        new("nav", "Missing deps", "missing"),
        new("nav", "Repositories", "repos"),
        new("nav", "Trash & backup", "trash"),
        new("nav", "Activity history", "history"),
        new("nav", "Settings", "settings"),
        new("action", "Index now", "index"),
        new("action", "Add repository", "add-repo"),
        new("action", "Rescue — get the game to launch", "rescue"),
    ];

    public async Task<IReadOnlyList<CommandHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var hits = new List<CommandHit>();
        if (string.IsNullOrWhiteSpace(query))
            return hits;

        foreach (var c in Registry)
            if (c.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                hits.Add(c);

        var page = await library.GetPageAsync(new LibraryQuery(Take: 8, SearchText: query), cancellationToken).ConfigureAwait(false);
        hits.AddRange(page.Items.Select(p => new CommandHit("package", p.VarName, p.PackageId.ToString())));
        return hits;
    }
}
