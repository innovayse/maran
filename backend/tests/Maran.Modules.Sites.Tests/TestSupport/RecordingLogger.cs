using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps the level, event id and text of every entry it is given.</summary>
/// <typeparam name="T">The logger's category type.</typeparam>
/// <remarks>
/// The level and the event id are kept, not only the text, because the handler's two compensation
/// reports are told apart by their event ids and both are errors an operator has to find.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Every entry written, in order.</summary>
    public List<LoggedEntry> Entries { get; } = [];

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
        Entries.Add(new LoggedEntry(logLevel, eventId.Id, formatter(state, exception)));
    }
}
