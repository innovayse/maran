using Microsoft.Extensions.Logging;

namespace Maran.Host.Tests.Fixtures;

/// <summary>One entry a <see cref="LogEntryRecorder{T}"/> was given.</summary>
/// <param name="Level">The severity the entry was written at.</param>
/// <param name="Message">The message, formatted with its arguments.</param>
/// <param name="Exception">The exception attached to the entry, or null.</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
