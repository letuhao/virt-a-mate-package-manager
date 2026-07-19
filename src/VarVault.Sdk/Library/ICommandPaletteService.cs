namespace VarVault.Sdk.Library;

/// <summary>A command-palette hit: a package, a navigation target, or an action.</summary>
public sealed record CommandHit(string Kind, string Title, string Target);

/// <summary>
/// BE-N14 · Global search / command palette (Ctrl-K). Combines package search (over the read model) with a
/// built-in registry of navigation targets and actions. No new persistence. (16-checklist BE-N14.)
/// </summary>
public interface ICommandPaletteService
{
    Task<IReadOnlyList<CommandHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
