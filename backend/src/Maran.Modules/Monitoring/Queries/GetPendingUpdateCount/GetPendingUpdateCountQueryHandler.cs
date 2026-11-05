using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;

namespace Maran.Modules.Monitoring.Queries.GetPendingUpdateCount;

/// <summary>Handles <see cref="GetPendingUpdateCountQuery"/> by counting what the agent reports (issue #324).</summary>
/// <remarks>
/// A failed read is the agent's own typed failure and never a count of zero: a badge reading 0 on a
/// host whose package cache could not be read would say "up to date" about something nobody checked.
/// The caller draws no badge when this fails, and a zero is only ever the answer to a read that
/// succeeded and found nothing pending. The security figure follows the same rule one level down: it
/// is <c>null</c> unless every pending update was classified, because a count over a partial reading
/// would understate and "0 security updates" is the answer this screen must not invent.
/// </remarks>
public sealed class GetPendingUpdateCountQueryHandler
{
    /// <summary>The agent, the only thing in the system that can ask the host's package manager.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the pending updates.</param>
    public GetPendingUpdateCountQueryHandler(IAgentMonitorClient agent)
    {
        _agent = agent;
    }

    /// <summary>Returns how many updates are pending.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The count, or the agent's own typed failure.</returns>
    public async Task<Result<PendingUpdateCountDto>> HandleAsync(
        GetPendingUpdateCountQuery query,
        CancellationToken cancellationToken)
    {
        var updates = await _agent.ListPendingUpdatesAsync(cancellationToken);

        if (!updates.IsSuccess)
        {
            return Result<PendingUpdateCountDto>.Fail(updates.Error!);
        }

        var anyUnmeasured = updates.Value.Any(update =>
        {
            return update.Classification == AgentUpdateClassification.Unmeasured;
        });

        int? securityCount = null;
        if (!anyUnmeasured)
        {
            securityCount = updates.Value.Count(update =>
            {
                return update.Classification == AgentUpdateClassification.Security;
            });
        }

        return Result<PendingUpdateCountDto>.Ok(new PendingUpdateCountDto(updates.Value.Count, securityCount));
    }
}
