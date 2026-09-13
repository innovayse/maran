using Microsoft.Extensions.Logging;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// An <see cref="ILogger{T}"/> double that keeps the formatted text of everything it was told, so a
/// test can assert on a line an operator will actually read.
/// </summary>
/// <typeparam name="T">The type the logger belongs to.</typeparam>
/// <remarks>
/// Used where the log line IS the outcome. A startup pass that skips an episode changes nothing a
/// caller can see; the one place an operator learns what happened is the message, and a message that
/// reports the wrong number is the whole finding rather than a cosmetic one.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Every message this logger has formatted, in order.</summary>
    public List<string> Messages { get; } = [];

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
        Messages.Add(formatter(state, exception));
    }
}
