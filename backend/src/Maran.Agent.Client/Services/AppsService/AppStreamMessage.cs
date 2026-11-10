using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AppsService;

/// <summary>
/// One message of an install or uninstall stream with the two wire shapes folded into one, so the
/// terminal-ending rules are written once and not once per direction.
/// </summary>
/// <param name="Branch">Which branch of the wire oneof the message carried.</param>
/// <param name="Progress">The progress payload when <paramref name="Branch"/> is progress; otherwise null.</param>
/// <param name="Error">The failure payload when <paramref name="Branch"/> is error; otherwise null.</param>
internal sealed record AppStreamMessage(AppStreamBranch Branch, Progress? Progress, AgentError? Error);
