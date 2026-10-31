using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One event from an install stream: progress, or the way the stream ended.</summary>
/// <param name="Kind">Whether this is progress or one of the five terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; zero otherwise.</param>
/// <param name="Stage">Machine-stable stage id for progress events, e.g. <c>installing</c>; empty otherwise.</param>
/// <param name="Version">The installed version on <see cref="PhpInstallEventKind.Installed"/>; empty otherwise.</param>
/// <param name="Error">
/// The typed failure for <see cref="PhpInstallEventKind.Failed"/> — its machine-stable code AND its
/// kind, exactly as the agent client decided them — and null for every other kind. The kind travels
/// with the code (issue #251) so a caller answers an upstream refusal, an unavailable agent and a
/// local fault each with its own status instead of rebuilding one kind for all of them. It carries
/// a code and never the agent's own sentence, which can name paths on the host.
/// </param>
public sealed record PhpInstallEvent(
    PhpInstallEventKind Kind,
    uint Percent,
    string Stage,
    string Version,
    Error? Error);
