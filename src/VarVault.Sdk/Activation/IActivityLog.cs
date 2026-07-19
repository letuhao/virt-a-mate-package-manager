namespace VarVault.Sdk.Activation;

/// <summary>An audited action shown in the activity history.</summary>
public sealed record ActivityRecord(string Kind, string Description, DateTime AtUtc);

/// <summary>Records and reads the audit history of moves/installs/deletes/fixes/aliases. (Checklist X.12.)</summary>
public interface IActivityLog
{
    Task RecordAsync(string kind, string description, long? packageId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);
}
