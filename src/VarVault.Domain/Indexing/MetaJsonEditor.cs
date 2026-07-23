using System.Text.Json;
using System.Text.Json.Nodes;
using VarVault.Common;
using VarVault.Domain.Dependencies;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Applies Edit-meta field changes onto a raw <c>meta.json</c> document while preserving unknown keys.
/// </summary>
public static class MetaJsonEditor
{
    public static Result<string> Apply(
        string rawJson,
        string? creatorName,
        string? packageName,
        string? licenseType,
        string? description,
        string? programVersion,
        IReadOnlyList<string> dependencyRefs)
    {
        Guard.NotNull(rawJson);
        Guard.NotNull(dependencyRefs);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>("meta.parse", ex.Message);
        }

        if (root is not JsonObject obj)
            return Result.Failure<string>("meta.parse", "meta.json root is not an object");

        SetString(obj, "creatorName", creatorName);
        SetString(obj, "packageName", packageName);
        SetString(obj, "licenseType", licenseType);
        SetString(obj, "description", description);
        SetString(obj, "programVersion", programVersion);

        // Keep prior nested license/deps for refs the user kept; only stub brand-new keys.
        var priorDeps = obj["dependencies"] as JsonObject;
        var deps = new JsonObject();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in dependencyRefs)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                continue;
            var parsed = DependencyRef.Parse(trimmed);
            if (parsed.IsFailure)
                return Result.Failure<string>(parsed.Error);
            if (!seen.Add(trimmed))
                continue;

            if (TryTakePriorDep(priorDeps, trimmed) is { } kept)
                deps[trimmed] = kept;
            else
            {
                deps[trimmed] = new JsonObject
                {
                    ["licenseType"] = "",
                    ["dependencies"] = new JsonObject(),
                };
            }
        }
        obj["dependencies"] = deps;

        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Clone an existing dependency node (exact key, else case-insensitive) so edits don't wipe nested data.</summary>
    private static JsonNode? TryTakePriorDep(JsonObject? priorDeps, string trimmed)
    {
        if (priorDeps is null)
            return null;
        if (priorDeps.TryGetPropertyValue(trimmed, out var exact) && exact is not null)
            return exact.DeepClone();
        foreach (var (key, node) in priorDeps)
        {
            if (node is not null && string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
                return node.DeepClone();
        }
        return null;
    }

    private static void SetString(JsonObject obj, string name, string? value)
    {
        if (value is null)
            return;
        obj[name] = value;
    }
}
