using Maran.Agent.Client.Interfaces;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// Asks the agent whether this host's name server software can sign zones. The answer is the agent's
/// observation of the daemon and never a version table kept here.
/// </summary>
public sealed class DnssecHostProbe
{
    /// <summary>The agent, which owns the daemon.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Creates the probe.</summary>
    /// <param name="agent">The agent client that reads the daemon.</param>
    public DnssecHostProbe(IAgentDnsClient agent)
    {
        _agent = agent;
    }

    /// <summary>Asks whether the daemon was seen to accept a signing policy.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The answer, or the agent's typed failure when it could not be asked.</returns>
    public async Task<Result<bool>> IsSupportedAsync(CancellationToken cancellationToken)
    {
        var status = await _agent.GetStatusAsync(cancellationToken);

        return status.IsSuccess
            ? Result<bool>.Ok(status.Value.DnssecSupported)
            : Result<bool>.Fail(status.Error!);
    }

    /// <summary>The same question for a screen: an agent that cannot be asked reads as "cannot sign".</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True only when the daemon was positively seen to accept a signing policy.</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var answer = await IsSupportedAsync(cancellationToken);

        return answer.IsSuccess && answer.Value;
    }
}
