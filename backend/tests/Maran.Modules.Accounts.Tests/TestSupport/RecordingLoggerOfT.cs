using Microsoft.Extensions.Logging;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps every formatted message it was given.
/// </summary>
/// <typeparam name="T">The category the logger belongs to, as the handler asks for it.</typeparam>
/// <remarks>
/// It exists because two of this module's operator-facing sentences reach the operator through the
/// LOG and nowhere else: the account lifecycle answers its caller with an error code and reports the
/// reason it refused into a log line. A test taking <c>NullLogger</c> cannot observe that sentence at
/// all, so it can only assert that a refusal happened — which is satisfied by a handler that refuses
/// with a wrong, misleading or empty reason.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Every message written through this logger, formatted, in order.</summary>
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
