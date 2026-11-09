using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One event from an extension-change stream: progress, or the way the stream ended.</summary>
/// <param name="Kind">Whether this is progress or one of the six terminal endings.</param>
/// <param name="Percent">Completion from 0 to 100 for progress events; 100 on changed, zero otherwise.</param>
/// <param name="Stage">Machine-stable stage id for progress events, e.g. <c>install</c>; empty otherwise.</param>
/// <param name="Version">The PHP version changed, on <see cref="PhpExtensionEventKind.Changed"/>; empty otherwise.</param>
/// <param name="Extension">The catalogue name changed, on <see cref="PhpExtensionEventKind.Changed"/>; empty otherwise.</param>
/// <param name="Enabled">The state the extension is now in, on <see cref="PhpExtensionEventKind.Changed"/>; false otherwise.</param>
/// <param name="Error">
/// The typed failure for <see cref="PhpExtensionEventKind.Failed"/> — its machine-stable code AND its
/// kind, exactly as the agent client decided them — and null for every other kind. It carries a code
/// and never the agent's own sentence, which can name paths on the host.
/// </param>
public sealed record PhpExtensionEvent(
    PhpExtensionEventKind Kind,
    uint Percent,
    string Stage,
    string Version,
    string Extension,
    bool Enabled,
    Error? Error);
