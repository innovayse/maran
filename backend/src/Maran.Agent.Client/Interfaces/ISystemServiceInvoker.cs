using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.SystemService.AgentSystemClient"/> and the transport that actually performs the
/// <c>SystemService.GetAgentInfo</c> call, so the response-to-<c>Result</c> mapping is testable
/// without a real gRPC channel.
/// </summary>
internal interface ISystemServiceInvoker
{
    /// <summary>Invokes the agent's identity handshake rpc.</summary>
    /// <param name="ct">Cancellation for the call.</param>
    /// <returns>The raw wire response; may carry either the <c>ok</c> or the <c>error</c> branch of its oneof.</returns>
    Task<GetAgentInfoResponse> GetAgentInfoAsync(CancellationToken ct);
}
