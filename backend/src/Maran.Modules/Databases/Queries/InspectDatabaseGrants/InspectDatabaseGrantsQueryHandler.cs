using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Services;

namespace Maran.Modules.Databases.Queries.InspectDatabaseGrants;

/// <summary>
/// Handles <see cref="InspectDatabaseGrantsQuery"/> by asking the agent for a report-only pass over
/// the database server's grant table.
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>DbContext</c>, and that is the honest shape.</b> The subject of this query is the database
/// server's own grant table, which the panel keeps no copy of; the panel's rows would answer a
/// different question (who asked for which database) and could not name a grant nobody asked for. So
/// this handler reads the host and nothing else, and the report it returns is a fact about the server
/// rather than about the panel.
/// </para>
/// <para>
/// <b>The report-only flag is passed as a constant, never from a caller.</b> A query that could be
/// asked to act would be a mutation wearing a read's name, reachable by a <c>GET</c>, and cacheable.
/// </para>
/// </remarks>
public sealed class InspectDatabaseGrantsQueryHandler
{
    /// <summary>The agent client that owns the database server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>Resolver for the two sentences each refused row carries.</summary>
    private readonly GrantRepairRefusalDisplayNames _refusalText;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the grant table.</param>
    /// <param name="refusalText">Resolver for the refusal name and the advice beside it.</param>
    public InspectDatabaseGrantsQueryHandler(
        IAgentDbClient agent,
        GrantRepairRefusalDisplayNames refusalText)
    {
        _agent = agent;
        _refusalText = refusalText;
    }

    /// <summary>Reports what a repair would change on this host.</summary>
    /// <param name="query">The (parameterless) inspection request.</param>
    /// <param name="cancellationToken">Cancels the agent call.</param>
    /// <returns>The census, or the agent's own typed failure.</returns>
    public async Task<Result<GrantRepairReportDto>> HandleAsync(
        InspectDatabaseGrantsQuery query,
        CancellationToken cancellationToken)
    {
        var report = await _agent.RepairGrantsAsync(reportOnly: true, cancellationToken);
        if (!report.IsSuccess)
        {
            return Result<GrantRepairReportDto>.Fail(report.Error!);
        }

        return Result<GrantRepairReportDto>.Ok(
            GrantRepairReportMapper.ToDto(report.Value, isReportOnly: true, _refusalText));
    }
}
