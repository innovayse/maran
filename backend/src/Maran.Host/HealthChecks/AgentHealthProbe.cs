using Maran.Agent.Client.Interfaces;

namespace Maran.Host.HealthChecks;

/// <summary>
/// Asks the agent whether it is reachable. Health answers must be fast and total: a missing socket,
/// a refused connection or a hung agent are all just "unavailable", never an exception that turns a
/// health probe into a 500 and takes the panel out of a load balancer for the wrong reason.
/// </summary>
public sealed class AgentHealthProbe
{
    /// <summary>Reported when the handshake succeeds within the timeout.</summary>
    public const string Connected = "connected";

    /// <summary>Reported for any failure, timeout, or missing agent.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// How long the handshake may take. Deliberately short: the panel must report its own state
    /// quickly even when the agent is missing or unresponsive.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The client used to reach the agent's system service.</summary>
    private readonly IAgentSystemClient _agentClient;

    /// <summary>Creates the probe around the agent client.</summary>
    /// <param name="agentClient">Client used to perform the handshake.</param>
    public AgentHealthProbe(IAgentSystemClient agentClient)
    {
        _agentClient = agentClient;
    }

    /// <summary>Performs the handshake and reports connectivity.</summary>
    /// <returns><see cref="Connected"/> on a successful handshake, otherwise <see cref="Unavailable"/>.</returns>
    public async Task<string> ProbeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            var result = await _agentClient.GetInfoAsync(cts.Token);
            return result.IsSuccess ? Connected : Unavailable;
        }
        catch (Exception)
        {
            // Every failure mode collapses to one answer on purpose: the caller of a health
            // endpoint can act on "unavailable", but not on a transport exception's details.
            return Unavailable;
        }
    }
}
