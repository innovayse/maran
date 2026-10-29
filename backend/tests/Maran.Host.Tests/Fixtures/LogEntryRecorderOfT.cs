using Microsoft.Extensions.Logging;

namespace Maran.Host.Tests.Fixtures;

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps every entry, with its level, for a test to read.</summary>
/// <typeparam name="T">The logger's category type.</typeparam>
internal sealed class LogEntryRecorder<T> : ILogger<T>
{
    /// <summary>The entries written so far, guarded by itself because a hosted service writes from another thread.</summary>
    private readonly List<LogEntry> _entries = [];

    /// <summary>A snapshot of every entry written so far, in order.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}
