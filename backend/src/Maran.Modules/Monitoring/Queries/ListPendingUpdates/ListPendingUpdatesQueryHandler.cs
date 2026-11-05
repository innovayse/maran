using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Resources;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// Handles <see cref="ListPendingUpdatesQuery"/> by asking the agent for the whole list of pending
/// updates and paging it in memory (issue #324).
/// </summary>
/// <remarks>
/// <para>
/// <b>A failed read is a failed request, never an empty page.</b> The agent reads the package
/// manager's local cache only; a cache it cannot read comes back as its own typed failure, and this
/// handler passes that failure on untouched. An empty page here would read as "nothing to update" on a
/// host whose security updates may be pending, which is the one answer this screen must never invent.
/// An empty page is only ever the answer to a read that succeeded and found nothing (or nothing that
/// matched the search), and it still carries the true total.
/// </para>
/// <para>
/// <b>Paged here, not by the agent.</b> The agent has no page parameter: a package manager answers its
/// whole list in one process run, and that run is the expensive part. The in-memory pager is the one
/// the rest of the panel uses for an agent list, so a search, a sort and a page past the end behave as
/// they do everywhere else. The classification filter narrows the whole list first, so the total a page
/// carries is the filtered total.
/// </para>
/// </remarks>
public sealed class ListPendingUpdatesQueryHandler
{
    /// <summary>What a search reads: the package name, and nothing else.</summary>
    private static readonly IReadOnlyList<Func<AgentPendingUpdate, string>> SearchFields =
    [
        update =>
        {
            return update.PackageName;
        },
    ];

    /// <summary>The agent, the only thing in the system that can ask the host's package manager.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>Names each row's classification in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the pending updates.</param>
    /// <param name="displayNames">This module's display names.</param>
    public ListPendingUpdatesQueryHandler(IAgentMonitorClient agent, IStringLocalizer<DisplayNames> displayNames)
    {
        _agent = agent;
        _displayNames = displayNames;
    }

    /// <summary>Returns the requested page of pending updates.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page with the total that matched, or the agent's own typed failure.</returns>
    public async Task<Result<PagedResult<PendingUpdateDto>>> HandleAsync(
        ListPendingUpdatesQuery query,
        CancellationToken cancellationToken)
    {
        var updates = await _agent.ListPendingUpdatesAsync(cancellationToken);

        if (!updates.IsSuccess)
        {
            return Result<PagedResult<PendingUpdateDto>>.Fail(updates.Error!);
        }

        IEnumerable<AgentPendingUpdate> rows = updates.Value;
        if (!query.Classification.IsNone)
        {
            var wanted = query.Classification.Value;
            rows = rows.Where(update =>
            {
                return PendingUpdateClassificationPolicy.NameOf(update.Classification) == wanted;
            });
        }

        var page = rows.ToPaged(query, ListPendingUpdatesSorts.Map, SearchFields);

        return Result<PagedResult<PendingUpdateDto>>.Ok(page.Map(update =>
        {
            return PendingUpdateMapper.ToDto(update, _displayNames);
        }));
    }
}
