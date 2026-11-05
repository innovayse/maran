using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>One log entry as a handler wrote it.</summary>
/// <param name="Level">The entry's severity.</param>
/// <param name="Text">The formatted message text.</param>
public sealed record LoggedMessage(LogLevel Level, string Text);
