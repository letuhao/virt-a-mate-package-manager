namespace VarVault.Common;

/// <summary>Testable time source. All timestamps in VarVault are UTC.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default clock backed by the system UTC time.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
