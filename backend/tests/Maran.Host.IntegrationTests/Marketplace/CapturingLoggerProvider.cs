using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// Keeps every log line written through the loggers it hands out - category, formatted message and the
/// full text of any exception - so a test can search everything the panel logged for a planted secret.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>The captured lines, in the order they were written.</summary>
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Every line captured so far, one per entry.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            return _lines.ToArray();
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _lines);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>A logger that appends what it is given to the provider's queue.</summary>
    private sealed class CapturingLogger : ILogger
    {
        /// <summary>The category the logger writes under.</summary>
        private readonly string _category;

        /// <summary>The queue the provider reads.</summary>
        private readonly ConcurrentQueue<string> _lines;

        /// <summary>Creates the logger.</summary>
        /// <param name="category">The category it writes under.</param>
        /// <param name="lines">The provider's queue.</param>
        public CapturingLogger(string category, ConcurrentQueue<string> lines)
        {
            _category = category;
            _lines = lines;
        }

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
            var message = formatter(state, exception);
            _lines.Enqueue(exception is null ? $"{_category}: {message}" : $"{_category}: {message} {exception}");
        }
    }
}
