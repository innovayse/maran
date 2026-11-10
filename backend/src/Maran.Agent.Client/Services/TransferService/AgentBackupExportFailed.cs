using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The terminal failure: the export did not complete, and what was received so far is partial.</summary>
/// <param name="Error">
/// The typed failure, a code and never the agent's own sentence. A stream that ended without the agent
/// stating an outcome carries <c>AgentInvalidResponse</c>; one whose transport broke carries
/// <c>AgentUnavailable</c>.
/// </param>
public sealed record AgentBackupExportFailed(Error Error) : AgentBackupExportEvent;
