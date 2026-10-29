using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>One entry a <see cref="RecordingLogger{T}"/> was given.</summary>
/// <param name="Level">The severity it was written at.</param>
/// <param name="EventId">The numeric event id of the log delegate that wrote it.</param>
/// <param name="Message">The message, already formatted with its arguments.</param>
public sealed record LoggedEntry(LogLevel Level, int EventId, string Message);
