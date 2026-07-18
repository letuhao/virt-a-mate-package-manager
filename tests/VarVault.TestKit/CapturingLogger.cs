using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VarVault.TestKit;

public readonly record struct LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>
/// A logger provider that records every entry, so tests can assert on what was logged.
/// Install via the host's configure seam: <c>services.AddLogging(b => b.AddProvider(provider))</c>.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);
    public void Dispose() { }

    public bool Any(LogLevel level) => Entries.Any(e => e.Level == level);
    public IEnumerable<LogEntry> OfLevel(LogLevel level) => Entries.Where(e => e.Level == level);

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new LogEntry(logLevel, category, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
