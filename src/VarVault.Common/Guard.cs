using System.Runtime.CompilerServices;

namespace VarVault.Common;

/// <summary>Argument validation helpers that throw on programmer error (not recoverable failure).</summary>
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be null or whitespace.", name)
            : value;

    public static int Positive(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");
}
