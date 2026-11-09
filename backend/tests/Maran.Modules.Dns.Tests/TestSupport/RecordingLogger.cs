using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps the level and text of every entry it is given.</summary>
/// <typeparam name="T">The logger's category type.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Every entry written, in order, as its level and its formatted message.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
