namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One event from an install stream: progress, or the way the stream ended.</summary>
/// <param name="Kind">Whether this is progress or one of the five terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; zero otherwise.</param>
/// <param name="Stage">Machine-stable stage id for progress events, e.g. <c>installing</c>; empty otherwise.</param>
/// <param name="Version">The installed version on <see cref="PhpInstallEventKind.Installed"/>; empty otherwise.</param>
/// <param name="ErrorCode">
/// The machine-stable error code for <see cref="PhpInstallEventKind.Failed"/>; null otherwise. It is
/// a code and never the agent's own sentence, which can name paths on the host.
/// </param>
public sealed record PhpInstallEvent(
    PhpInstallEventKind Kind,
    uint Percent,
    string Stage,
    string Version,
    string? ErrorCode);
