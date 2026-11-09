using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Queries.GetDnsStatus;

/// <summary>Handles <see cref="GetDnsStatusQuery"/> by asking the agent, and never from a stored copy.</summary>
/// <remarks>
/// What the daemon is doing is read back on every call and never stored, which is the distinction that keeps
/// a status screen from agreeing with the panel by construction. An agent that cannot be asked is answered as
/// <c>AgentReachable = false</c> beside the stored intention, not as a failure: a screen must be able to say
/// "I could not look", which is different from "stopped".
/// </remarks>
public sealed class GetDnsStatusQueryHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The agent, which owns the daemon.</summary>
    private readonly IAgentDnsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="agent">The agent client that reads the daemon.</param>
    public GetDnsStatusQueryHandler(DnsDbContext dbContext, IAgentDnsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Returns the intention beside the observation.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The status; this operation never fails.</returns>
    public async Task<Result<DnsStatusDto>> HandleAsync(GetDnsStatusQuery query, CancellationToken cancellationToken)
    {
        var enabled = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.Enabled)
            .SingleOrDefaultAsync(cancellationToken);

        var observed = await _agent.GetStatusAsync(cancellationToken);

        if (!observed.IsSuccess)
        {
            return Result<DnsStatusDto>.Ok(DnsStatusMapper.Unreachable(enabled));
        }

        // The count comes from the agent's list, and a list that cannot be read leaves it at zero rather than
        // turning a readable status into an unreachable one.
        var zones = await _agent.ListZonesAsync(cancellationToken);

        return Result<DnsStatusDto>.Ok(DnsStatusMapper.Observed(enabled, observed.Value, zones.IsSuccess ? zones.Value.Count : 0));
    }
}
