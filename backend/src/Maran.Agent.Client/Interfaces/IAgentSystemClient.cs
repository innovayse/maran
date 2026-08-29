using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>Typed access to the agent's SystemService.</summary>
public interface IAgentSystemClient
{
    /// <summary>Performs the identity handshake with the local agent.</summary>
    /// <param name="ct">Cancellation for the call.</param>
    /// <returns>The agent's identity on success, or a typed <c>agent.*</c> error.</returns>
    Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct);
}
