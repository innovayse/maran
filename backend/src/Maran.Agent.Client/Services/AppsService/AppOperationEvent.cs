using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.AppsService;

/// <summary>One event from an install or uninstall stream: progress, or the way the stream ended.</summary>
/// <param name="Kind">Whether this is progress or one of the six terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; zero otherwise, 100 on completion.</param>
/// <param name="Stage">Machine-stable stage id for progress events, e.g. <c>installing</c>; empty otherwise.</param>
/// <param name="Error">
/// The typed failure for <see cref="AppOperationEventKind.Failed"/>, its machine-stable code AND its
/// kind exactly as the agent client decided them, and null for every other kind. It carries a code and
/// never the agent's own sentence, which can name paths on the host.
/// </param>
public sealed record AppOperationEvent(
    AppOperationEventKind Kind,
    uint Percent,
    string Stage,
    Error? Error);
