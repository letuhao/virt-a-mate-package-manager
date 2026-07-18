using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;

namespace VarVault.Domain.Content;

/// <summary>
/// 🔒 Validates that a var satisfies the constraints VaM needs to load it: <c>meta.json</c> present,
/// and every non-ASCII entry name carries the UTF-8 flag (bit 11) with healthy encoding. Used to prove
/// an encoding-fixed var is good before it is preferred over the original. (Checklist 4.13.)
/// </summary>
public static class VamVarValidator
{
    public static Result Validate(IReadOnlyList<ZipEntryFacts> entries)
    {
        Guard.NotNull(entries);

        if (!entries.Any(e => e.IsRootMetaJson))
            return Result.Failure("var.validate.meta", "meta.json is missing");

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
                continue;
            if (!IsPureAscii(entry.RawNameBytes) && !entry.NameIsUtf8)
                return Result.Failure("var.validate.utf8", "a non-ASCII entry name is not flagged UTF-8");
        }

        var health = EncodingHealthEngine.Detect(entries);
        return health.Health is EncodingHealth.Ok
            ? Result.Success()
            : Result.Failure("var.validate.encoding", $"encoding not healthy: {health.Health}");
    }

    private static bool IsPureAscii(byte[] raw)
    {
        foreach (var b in raw)
        {
            if (b >= 0x80)
                return false;
        }
        return true;
    }
}
