using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Queries.InspectAccountHomeGroups;

/// <summary>
/// Handles <see cref="InspectAccountHomeGroupsQuery"/> by asking the agent for a report-only pass over
/// every hosting account's home directory group.
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>DbContext</c>, and that is the honest shape.</b> The subject of this query is the host's
/// own password database and filesystem, which the panel keeps no copy of; the panel's own account
/// rows would answer a different question (which accounts the panel believes it created) and could
/// not name a home whose group the agent finds wrong on disk.
/// </para>
/// <para>
/// <b>The report-only flag is passed as a constant, never from a caller.</b> A query that could be
/// asked to act would be a mutation wearing a read's name, reachable by a <c>GET</c>, and cacheable.
/// </para>
/// </remarks>
public sealed class InspectAccountHomeGroupsQueryHandler
{
    /// <summary>The agent client that owns the host's accounts.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Resolver for the two sentences each refused home carries.</summary>
    private readonly HomeGroupRepairRefusalDisplayNames _refusalText;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the host's accounts.</param>
    /// <param name="refusalText">Resolver for the refusal name and the advice beside it.</param>
    public InspectAccountHomeGroupsQueryHandler(
        IAgentAccountsClient agent,
        HomeGroupRepairRefusalDisplayNames refusalText)
    {
        _agent = agent;
        _refusalText = refusalText;
    }

    /// <summary>Reports what a repair would change on this host.</summary>
    /// <param name="query">The (parameterless) inspection request.</param>
    /// <param name="cancellationToken">Cancels the agent call.</param>
    /// <returns>The census, or the agent's own typed failure.</returns>
    public async Task<Result<HomeGroupRepairReportDto>> HandleAsync(
        InspectAccountHomeGroupsQuery query,
        CancellationToken cancellationToken)
    {
        var report = await _agent.RepairHomeGroupsAsync(reportOnly: true, cancellationToken);
        if (!report.IsSuccess)
        {
            return Result<HomeGroupRepairReportDto>.Fail(report.Error!);
        }

        return Result<HomeGroupRepairReportDto>.Ok(
            HomeGroupRepairReportMapper.ToDto(report.Value, isReportOnly: true, _refusalText));
    }
}
