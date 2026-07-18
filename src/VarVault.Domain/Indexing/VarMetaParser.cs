using System.Text.Json;
using VarVault.Common;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Parses a var's <c>meta.json</c> text into <see cref="VarMeta"/> — tolerant of missing fields and
/// returning a typed failure (never throwing) on malformed JSON. (IDX-1/4.)
/// </summary>
public static class VarMetaParser
{
    public static Result<VarMeta> Parse(string json)
    {
        Guard.NotNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Result.Failure<VarMeta>("meta.parse", "meta.json root is not an object");

            var deps = new List<string>();
            if (root.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in d.EnumerateObject())
                    deps.Add(prop.Name);
            }

            return new VarMeta(
                Creator: GetString(root, "creatorName"),
                Package: GetString(root, "packageName"),
                LicenseType: GetString(root, "licenseType"),
                Description: GetString(root, "description"),
                ProgramVersion: GetString(root, "programVersion"),
                DependencyRefs: deps);
        }
        catch (JsonException ex)
        {
            return Result.Failure<VarMeta>("meta.parse", ex.Message);
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
