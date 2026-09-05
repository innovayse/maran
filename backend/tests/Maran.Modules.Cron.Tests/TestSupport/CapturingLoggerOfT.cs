using Microsoft.Extensions.Logging;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps every line it was asked to write, fully
/// rendered.
/// </summary>
/// <remarks>
/// It exists for one rule and would not otherwise be worth writing: no log line this module produces
/// may contain a customer's cron command, at any level and on any path (RULING 31). A test can only
/// assert that about lines it can read, and <c>NullLogger</c> — what every other module's tests
/// inject — reads back nothing at all, so a handler that logged the command would pass every test in
/// the suite.
///
/// The message is rendered through the framework's own formatter, so what is captured is the string
/// a sink would write, with the structured values substituted into it. Capturing the template alone
/// would be exactly the mistake this type exists to prevent: the template never contains the
/// command, and the rendered line is where it would appear.
///
/// It is enabled at every level on purpose. The rule says "at any level", and a capture that
/// answered false to <see cref="IsEnabled"/> for <c>Trace</c> would quietly stop covering the debug
/// line somebody adds later.
/// </remarks>
/// <typeparam name="TCategory">The type the logger is categorised by, as the framework requires.</typeparam>
public sealed class CapturingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Every line written through this logger, fully rendered, in order.</summary>
    public List<string> Lines { get; } = [];

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
        Lines.Add(formatter(state, exception));
    }
}
