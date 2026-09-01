using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>An <see cref="ILogger{TCategoryName}"/> that keeps every formatted message it is given.</summary>
/// <typeparam name="T">The logger's category type.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Every message written, in order, already formatted with its arguments.</summary>
    public List<string> Messages { get; } = [];

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
        Messages.Add(formatter(state, exception));
    }
}
