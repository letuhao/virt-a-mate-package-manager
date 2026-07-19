namespace VarVault.Domain.Entities;

/// <summary>
/// An audited action (move/install/delete/fix/alias). Append-only history for the activity screen.
/// <see cref="TimestampUnixMs"/> is a UTC epoch. (Checklist X.12.)
/// </summary>
public sealed class ActivityEntry
{
    public long Id { get; set; }
    public long TimestampUnixMs { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long? PackageId { get; set; }
}
