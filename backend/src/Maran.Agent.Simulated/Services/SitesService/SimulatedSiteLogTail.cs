using System.Runtime.CompilerServices;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.SitesService;

/// <summary>
/// The events of tailing a simulated site's log: a bounded replay of recent lines, oldest first, and
/// exactly one terminal event naming how the stream ended.
/// </summary>
/// <remarks>
/// The simulated log has nothing more to say after its history, so a tail ends as <c>Completed</c>.
/// A site that is not there ends the stream with a failed event carrying <c>AgentNotFound</c>, and a
/// caller that stops watching is told so with <c>Cancelled</c>: the sequence never ends quietly.
/// </remarks>
public sealed class SimulatedSiteLogTail
{
    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of the moment the newest line is stamped with.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a tail source over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <param name="clock">The source of the moment the newest line is stamped with.</param>
    public SimulatedSiteLogTail(SimulatedAgentLedger ledger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);

        _ledger = ledger;
        _clock = clock;
    }

    /// <summary>Produces the history of a log followed by exactly one terminal event.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="logSource">Which log to read.</param>
    /// <param name="historyLines">How many lines to replay, before the cap.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>The lines, then the ending.</returns>
    public async IAsyncEnumerable<SiteLogEvent> EventsAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The first line is produced after a real asynchronous boundary, as a read from a socket would
        // be, so a caller that cancels between starting and reading is observed, not skipped.
        await Task.Yield();

        if (!Enum.IsDefined(logSource) || !IsOwnedSite(accountUsername, domain))
        {
            yield return new SiteLogFailed(SimulatedAgentErrors.NotFoundCode);
            yield break;
        }

        var count = (int)Math.Min(historyLines, _ledger.Limits.MaxLogHistoryLines);
        var now = _clock.UtcNow;
        for (var index = count - 1; index >= 0; index--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new SiteLogEnded(SiteLogEndKind.Cancelled);
                yield break;
            }

            yield return new SiteLogLine(SimulatedSiteLog.Line(domain, logSource, index, now), true);
        }

        yield return new SiteLogEnded(
            cancellationToken.IsCancellationRequested ? SiteLogEndKind.Cancelled : SiteLogEndKind.Completed);
    }

    /// <summary>Whether the site exists and belongs to the account.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <returns>True when the account owns a site with that primary domain.</returns>
    private bool IsOwnedSite(string accountUsername, string domain)
    {
        return _ledger.Vhosts.TryGet(domain, out var vhost)
            && string.Equals(vhost.AccountUsername, accountUsername, StringComparison.Ordinal);
    }
}
