using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Asks the agent to remove one site's rotated log archives beyond the site's own retention, and records
/// it when something was removed (issue #152).
/// </summary>
/// <remarks>
/// <para>
/// The installer's shared log rotation keeps fourteen archives for every site and is the hard bound; this
/// only ever asks for fewer, so a mistake here costs disk by at most those fourteen files and never
/// more. The agent removes only archives numbered above the retention that belong to this site's own
/// files; the panel passes the number and nothing else.
/// </para>
/// <para>
/// An entry is written only when files were removed, as the panel's own actor: removing an old customer
/// file with nobody having asked for it that hour is the event an operator may later need to find, and an
/// hour in which nothing was removed is not.
/// </para>
/// </remarks>
public sealed class SiteLogPruner
{
    /// <summary>The agent, which owns the log files.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the pruner.</summary>
    /// <param name="agent">The agent client that removes the archives.</param>
    /// <param name="journal">This module's audit journal.</param>
    public SiteLogPruner(IAgentSitesClient agent, SiteAuditJournal journal)
    {
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Prunes one site's archives to its stored retention.</summary>
    /// <param name="site">The site, loaded by the caller.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the agent call.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>Success, or the agent's typed failure.</returns>
    public async Task<Result> PruneAsync(Site site, string accountUsername, CancellationToken cancellationToken)
    {
        var pruned = await _agent.PruneLogsAsync(
            accountUsername, site.Domain, (uint)site.LogRetentionDays, cancellationToken);
        if (!pruned.IsSuccess)
        {
            return Result.Fail(pruned.Error!);
        }

        if (pruned.Value.RemovedFiles > 0)
        {
            await _journal.RecordSystemAsync(SiteAuditActions.SiteLogsPruned, site.Domain, true, cancellationToken);
        }

        return Result.Ok();
    }
}
