using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Firewall.Services;

/// <summary>
/// Re-applies every ban that should still be in force, once the panel has started.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes a ban survive a reboot, and nothing else does.</b> Both supported families'
/// nftables units flush the ruleset on stop and on reload, and the agent keeps no ban state of its
/// own — so after a restart the kernel holds nothing, while <c>firewall.BanEpisodes</c> still knows
/// who was banned and until when. Without this class every ban the panel has ever placed silently
/// ends at the next restart, and the only symptom is an attacker getting back in.
/// </para>
/// <para>
/// <b>The REMAINING time is what is re-applied.</b> A panel restarted twenty-three hours into a
/// twenty-four-hour ban that asked for twenty-four hours again would hold the address for
/// forty-seven, and a machine that restarts often enough would hold it forever — a permanent ban
/// assembled out of temporary ones, with nothing in the journal saying so. An episode that has
/// already run out is SKIPPED, which is the same rule read from the other end: re-banning it would
/// resurrect a ban the clock had already ended.
/// </para>
/// <para>
/// <b>Re-applying a ban is not journalled; DECLINING to is.</b> A restored ban is not a new decision
/// — the decision is the episode, which was journalled when it was made — and an entry per restart
/// per banned address would bury the entries that record actual decisions. An episode this pass
/// declines to restore because the address has since been whitelisted IS a new decision, and it ends
/// the ban: the row is lifted and one <c>BanSkippedWhitelisted</c> entry records it, exactly as the
/// detector's handler records the same decision. Without that the skip was invisible three times
/// over — no entry, no log line of its own, and <c>GET /firewall/bans</c> still listing an address
/// the host was not holding — and deleting the whitelist row later resurrected eighteen hours of a
/// ban that had not been in effect for four.
/// </para>
/// <para>
/// <b>An agent that is not up yet is expected.</b> The panel and the agent are separate units and
/// there is no ordering guarantee that survives a reboot, so the first pass may find nothing
/// listening. It retries a bounded number of times and then stops: a pass that has failed
/// <see cref="MaximumAttempts"/> times is a broken host rather than a slow start, and a service that
/// retried forever would hide that behind an error line every thirty seconds.
/// </para>
/// </remarks>
public sealed class StartupBanReconciler : BackgroundService
{
    /// <summary>How many times a failed pass is retried before the reconciler gives up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    /// <remarks>
    /// The exempt count is printed and NOT netted out of the total. Subtracting it turned one
    /// in-force episode that was skipped into "Re-applied 0 of 0", which reads exactly like a panel
    /// with no bans at all — the one line an operator has about this pass, describing the wrong
    /// server.
    /// </remarks>
    private static readonly Action<ILogger, int, int, int, Exception?> LogReconciled =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(StartupBanReconciler)),
            "Re-applied {Reapplied} of {InForce} firewall bans that outlived the last restart; "
            + "{Exempt} were ended instead because the address is now whitelisted");

    /// <summary>Pre-compiled log delegate for a pass the agent would not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(2, nameof(StartupBanReconciler)),
            "Could not re-apply every firewall ban on attempt {Attempt}; banned addresses are reaching this host");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(3, nameof(StartupBanReconciler)),
            "Gave up re-applying firewall bans after {Attempts} attempts; every banned address is "
            + "reaching this host until the panel is restarted or the bans are re-applied by hand");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <c>FirewallDbContext</c>. A <see cref="BackgroundService"/> is a
    /// singleton, the context is scoped, and a singleton capturing a scoped dependency is refused by
    /// the container at BUILD time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<StartupBanReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="clock">The panel's clock, which decides what has already run out.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public StartupBanReconciler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<StartupBanReconciler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Re-applies every ban that is still in force, once.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>
    /// True when every episode still in force was re-applied — including when there were none.
    /// False when the agent refused at least one, which is the signal to try again.
    /// </returns>
    /// <remarks>
    /// One refusal does not abandon the pass. The agent can refuse a single address (a row somehow
    /// holding a form it will not accept) while every other ban goes in perfectly, and stopping at
    /// the first would leave the rest of the server unprotected because of one bad row.
    ///
    /// Public because it is the pass, and the pass is what has behaviour worth asserting: a test
    /// drives it directly rather than starting a hosted service and waiting for a timer, which is
    /// the sleep rules/testing.md forbids.
    /// </remarks>
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentFirewallClient>();
        var guard = scope.ServiceProvider.GetRequiredService<WhitelistGuard>();
        var journal = scope.ServiceProvider.GetRequiredService<FirewallAuditJournal>();

        var now = _clock.UtcNow;

        // Tracked, unlike every other read in this module, because an exempt episode is LIFTED
        // below: the pass does not merely decline to restore it, it records that the ban ended.
        var candidates = await dbContext.BanEpisodes
            .Where(episode => episode.LiftedAt == null && (episode.ExpiresAt == null || episode.ExpiresAt > now))
            .ToListAsync(cancellationToken);

        // The WHERE clause narrows the read; the entity decides. An episode with less than a whole
        // second left cannot be expressed on the wire — where 0 seconds means PERMANENT — so
        // re-applying it would install the opposite of what the row says.
        var inForce = candidates.Where(episode =>
        {
            return episode.IsInForce(now);
        }).ToList();

        // Once, before the loop. Per episode this was N sequential round trips to PostgreSQL before
        // a single ban was re-installed — paid in the exact window this class exists to close, on
        // the reboot after a botnet wave, which is when the table is largest.
        var whitelist = await guard.SnapshotAsync(cancellationToken);

        var reapplied = 0;
        var exempt = 0;
        var restorable = new List<BanEpisode>();
        foreach (var episode in inForce)
        {
            // R8 says the whitelist is checked before every automatic ban, and restoring one is
            // still placing one. This pass used to skip the check entirely, so an operator who
            // whitelisted themselves stayed exempt only until the panel restarted — the list held
            // while the process ran and was ignored the moment it came back.
            if (IsExemptFromRestoring(episode, whitelist))
            {
                // The ban is over: nothing on the host holds it and no later pass will restore it,
                // so the row says so rather than describing a ban that exists nowhere. It also
                // stops a whitelist row deleted tomorrow from resurrecting the remainder.
                episode.Lift(now);
                exempt++;

                await journal.RecordSystemAsync(
                    AuditActions.BanSkippedWhitelisted, episode.IpAddress, succeeded: true, cancellationToken);

                continue;
            }

            restorable.Add(episode);
        }

        // One call per ADDRESS, not per row. The host holds one set element per address and a second
        // `add element` replaces its timeout, so a pass that re-applied every row would install them
        // in list order and leave whichever it happened to read last — an address carrying an
        // operator's permanent episode and a detector's fifteen-minute one would come back from a
        // reboot holding either, at random. The ban in force is the LONGEST of an address's in-force
        // episodes, and that is what is restored. Every row of the group counts as re-applied,
        // because the one element that was installed is the ban all of them describe.
        foreach (var group in restorable.GroupBy(episode =>
        {
            return episode.IpAddress;
        }, StringComparer.Ordinal))
        {
            var banned = await agent.BanAsync(group.Key, LongestRemaining(group, now), cancellationToken);
            if (banned.IsSuccess)
            {
                reapplied += group.Count();
            }
        }

        if (exempt > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        LogReconciled(_logger, reapplied, inForce.Count, exempt, null);

        // An exempt episode is not a failure: it was deliberately not re-applied, so counting it as
        // one would make the pass retry five times and then report a broken host.
        return reapplied + exempt == inForce.Count;
    }

    /// <summary>Runs passes until one succeeds or the attempts run out.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (await AttemptAsync(attempt, stoppingToken))
                {
                    return;
                }

                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }

            LogGaveUp(_logger, MaximumAttempts, null);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one: a hosted service that
            // reported an error on every clean stop trains an operator to ignore its errors.
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a failed attempt.</summary>
    /// <param name="attempt">Which attempt this is, so the log line names it.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>True when the pass re-applied everything it found.</returns>
    /// <remarks>
    /// A database that is not reachable yet, or a socket that is not there, arrives here as an
    /// exception rather than as a failed <c>Result</c>. Letting it escape
    /// <see cref="ExecuteAsync"/> would stop the service for the lifetime of the process — which is
    /// the same outcome as never having written it, on the one boot where it mattered most.
    /// </remarks>
    private async Task<bool> AttemptAsync(int attempt, CancellationToken stoppingToken)
    {
        try
        {
            if (await ReconcileAsync(stoppingToken))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAttemptFailed(_logger, attempt, exception);
            return false;
        }

        LogAttemptFailed(_logger, attempt, null);
        return false;
    }

    /// <summary>Whether <paramref name="episode"/> must not be restored because it is now exempt.</summary>
    /// <param name="episode">The episode this pass is deciding about.</param>
    /// <param name="whitelist">The exempt ranges, read once for the whole pass.</param>
    /// <returns>True when the ban is automatic and the address has since been whitelisted.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only an AUTOMATIC ban is skipped.</b> The whitelist exempts an address from the
    /// brute-force detector, not from an administrator who decided to block it — which is what
    /// <c>WhitelistEntry</c> promises and what <c>BanAddressCommandHandler</c> already does on the
    /// way in. Skipping a manual ban here would let a reboot quietly undo a decision a person made,
    /// and with the lift above it would undo it permanently.
    /// </para>
    /// <para>
    /// An address the normalizer cannot read is not exempt and is sent to the agent, which refuses
    /// it: a row this panel cannot parse must not be silently forgiven into "exempt".
    /// </para>
    /// </remarks>
    private static bool IsExemptFromRestoring(BanEpisode episode, IReadOnlyList<WhitelistEntry> whitelist)
    {
        return episode.Reason == BanReason.BruteForce
            && IpAddressNormalizer.TryNormalize(episode.IpAddress, out var address)
            && WhitelistGuard.Exempts(whitelist, address);
    }

    /// <summary>How much of an address's ban is still to run, taking every row that describes it.</summary>
    /// <param name="episodes">Every in-force episode for one address.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>The longest remaining duration, or <c>null</c> when any of them is permanent.</returns>
    /// <remarks>
    /// The panel can hold more than one in-force episode for an address — an operator's ban and a
    /// detector's, placed independently and by design not collapsed into one row, because the
    /// detector's is keyed by its detection window and is what the escalation ladder counts. The host
    /// cannot hold more than one, so the ban that is in force is the longest of them; a permanent
    /// episode beats every timed one, which is what makes an operator's decision survive a restart no
    /// matter what the detector wrote beside it.
    /// </remarks>
    private static TimeSpan? LongestRemaining(IEnumerable<BanEpisode> episodes, DateTimeOffset now)
    {
        var longest = TimeSpan.Zero;

        foreach (var episode in episodes)
        {
            var remaining = episode.RemainingTtl(now);
            if (remaining is null)
            {
                // Nothing outlasts a ban with no end, so the answer is settled.
                return null;
            }

            if (remaining.Value > longest)
            {
                longest = remaining.Value;
            }
        }

        return longest;
    }
}
