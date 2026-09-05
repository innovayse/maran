using Maran.Agent.Client.Interfaces;
using Maran.Modules.Monitoring.Common;

namespace Maran.Modules.Monitoring.Queries.ListServiceStatuses;

/// <summary>Handles <see cref="ListServiceStatusesQuery"/> by asking the agent about its units.</summary>
/// <remarks>
/// <para>
/// <b>The three-valued state survives the projection.</b> Both the service name and the state cross
/// as their own names rather than as a boolean, because "not known" is a real answer the agent
/// works to produce: a socket-activated SSH unit on the Debian family is inactive from boot until
/// the first connection, and a panel that rendered that as an outage would report one on every such
/// host at every reboot.
/// </para>
/// <para>
/// <b>The names cross as enum members, not as <c>ToString()</c>.</b> The panel serializes an enum
/// through one camelCase converter registered for both of its JSON surfaces; a string built here
/// would arrive at the client in the member's own PascalCase instead, so this one module would
/// answer <c>Running</c> beside its own <c>lastDay</c>. Projecting the values as themselves leaves
/// the casing to the single place that decides it.
/// </para>
/// <para>
/// <b>Absence is meaningful and is left as absence.</b> The agent reports only the units it actually
/// watches, so a service with no row here is one this host does not observe — the interface must
/// read that as "not known", which is exactly what a missing row says. Fabricating a row for every
/// member of the enum would turn "we do not watch this" into "we watched it and it was fine".
/// </para>
/// </remarks>
public sealed class ListServiceStatusesQueryHandler
{
    /// <summary>The agent, which is the only thing in the system that can reach the service manager.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the service statuses.</param>
    public ListServiceStatusesQueryHandler(IAgentMonitorClient agent)
    {
        _agent = agent;
    }

    /// <summary>Returns one row per service the agent watches.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The statuses, or the agent's own typed failure.</returns>
    public async Task<Result<IReadOnlyList<ServiceStatusDto>>> HandleAsync(
        ListServiceStatusesQuery query,
        CancellationToken cancellationToken)
    {
        var statuses = await _agent.GetServiceStatusesAsync(cancellationToken);

        if (!statuses.IsSuccess)
        {
            return Result<IReadOnlyList<ServiceStatusDto>>.Fail(statuses.Error!);
        }

        var projected = statuses.Value
            .Select(status =>
            {
                return new ServiceStatusDto(status.Service, status.State, status.Detail);
            })
            .ToList();

        return Result<IReadOnlyList<ServiceStatusDto>>.Ok(projected);
    }
}
