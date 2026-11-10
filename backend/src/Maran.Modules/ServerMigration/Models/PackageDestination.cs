namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// Where a package is written, and how the writer tells the transport the exact size before the first byte
/// (issue #127).
/// </summary>
/// <remarks>
/// A package is streamed as the agent reads the artifact, never held whole; yet its size is known before the first
/// byte, because the head is built first and the artifact's length is in the agent's signed manifest. Telling the
/// transport lets a download carry a <c>Content-Length</c>, so the receiving side can show progress and tell a
/// truncated transfer from a finished one. A destination that has no use for the size passes a no-op.
/// </remarks>
/// <param name="Body">The stream the package is written to, in order and never read; server-established and never bound.</param>
/// <param name="OnLengthKnown">Called once, before the first byte, with the number of bytes the writer is about to write.</param>
public sealed record PackageDestination(Stream Body, Action<long> OnLengthKnown);
