using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The terminal failure: the read did not complete, and what was received so far is partial.</summary>
/// <param name="Error">
/// The typed failure, a code and never the agent's own sentence. A stream that ended without the agent
/// stating an outcome carries <c>AgentInvalidResponse</c>; one whose transport broke carries
/// <c>AgentUnavailable</c>.
/// </param>
/// <param name="Reason">Why the source refused when the agent said; otherwise <see cref="SourceFailureReason.Unspecified"/>.</param>
public sealed record AgentSourceInventoryFailed(Error Error, SourceFailureReason Reason) : AgentSourceInventoryEvent;
