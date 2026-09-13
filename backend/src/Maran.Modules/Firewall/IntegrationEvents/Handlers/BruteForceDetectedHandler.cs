using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Firewall.IntegrationEvents.Handlers;

/// <summary>
/// Turns a <see cref="BruteForceDetected"/> announcement into a ban on the host — unless the address
/// is whitelisted, in which case it turns it into the journal entry explaining why there is no ban
/// (spec §15).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whitelist is checked before anything else happens, and it is the only reason this panel
/// cannot ban its own operator.</b> An administrator mistyping a password from the office is
/// indistinguishable, at the detector, from an attack; the difference is a row somebody put on the
/// whitelist beforehand, and the installer seeds the first one with the address the install was run
/// from precisely so that a day-one server is not one typo away from being unreachable.
/// </para>
/// <para>
/// <b>A skipped ban is journalled, not silent.</b> <c>BanSkippedWhitelisted</c> is its own action
/// rather than a failure, because nothing went wrong: the absence of a ban an operator expected is
/// exactly what the entry explains, and without it a whitelist that had quietly grown too wide would
/// look like a detector that had stopped working.
/// </para>
/// <para>
/// <b>The handler is idempotent on (address, window).</b> A durable queue may deliver the same
/// detection twice — that is the queue behaving correctly — and a second delivery must extend
/// nothing and count as no second offence, or a redelivery storm would escalate an address to a
/// twenty-four-hour ban on its first mistake. The check is a read followed by a write, so two
/// SIMULTANEOUS deliveries can both pass it; the database's unique index on (IpAddress, WindowStart)
/// is what actually enforces the rule, and the loser of that race is caught and logged here rather
/// than thrown, for the reason in the next paragraph.
/// </para>
/// <para>
/// <b>An automatic ban never shortens one that is already standing.</b> The host holds one set
/// element per address and a second <c>add element</c> replaces its timeout — permanent included —
/// so the ladder's rung is raised to whatever of this address's in-force episodes has furthest to
/// run before the agent is called. See <see cref="LongerOf"/>.
/// </para>
/// <para>
/// <b>This handler writes exactly one row: its own.</b> It reads the standing episodes and edits
/// none of them, which is what makes an operator's ban win as a property of the DATA rather than of
/// timing. Both this handler and the manual one write <c>firewall.BanEpisodes</c> for the same
/// address with no transaction, no row lock and no concurrency token; if an automatic ban could
/// reschedule the row a manual ban owns, then two overlapping deliveries would be settled by
/// whichever <c>SaveChangesAsync</c> committed last, and the very defect this rule exists to close —
/// an operator's long ban cut to the ladder's rung — would come back through the concurrent door.
/// Because the automatic path only ever INSERTS, keyed uniquely by (<c>IpAddress</c>,
/// <c>WindowStart</c>), the two paths touch disjoint rows and the panel's answer for an address is
/// the same whichever order they commit in. This is the same deference the whitelist already
/// expresses in the other direction: an automatic ban consults it and a manual one does not, because
/// a reflex defers to a decision.
/// </para>
/// <para>
/// <b>What an address's rows mean together.</b> The host holds one element per address, and the
/// panel may hold several in-force episodes describing it, so the ban in force is the LONGEST of
/// them — a permanent one beating every timed one. That is the rule this handler asks the agent for
/// and the rule <c>StartupBanReconciler</c> restores after a restart. It is not a weaker invariant
/// than one row per address: it is the one that survives two independent writers.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> An unhandled exception would put the message back on the queue and
/// the whole thing would be retried, which for a ban means the escalation ladder being climbed by
/// the retry loop rather than by the attacker. A refusal is logged and journalled and the message is
/// done with.
/// </para>
/// </remarks>
public sealed class BruteForceDetectedHandler
{
    /// <summary>How far back the escalation ladder counts an address's previous bans.</summary>
    /// <remarks>
    /// A day, matching the longest rung. Shorter and a persistent attacker resets to fifteen minutes
    /// between waves; longer and somebody who mistyped a password on Monday is still paying for it
    /// on Friday.
    /// </remarks>
    private static readonly TimeSpan EscalationWindow = TimeSpan.FromHours(24);

    /// <summary>Pre-compiled log delegate for a detection naming something that is not an address.</summary>
    private static readonly Action<ILogger, Exception?> LogUnusableAddress =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(BruteForceDetectedHandler)),
            "A brute-force detection carried an address this panel cannot ban; nothing was banned");

    /// <summary>Pre-compiled log delegate for a ban the agent refused.</summary>
    private static readonly Action<ILogger, string, Exception?> LogBanRefused =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(BruteForceDetectedHandler)),
            "The agent refused an automatic ban with {AgentErrorCode}; the address is still reaching this host");

    /// <summary>Pre-compiled log delegate for an episode the database would not store.</summary>
    /// <remarks>
    /// Warning and not Error, with the exception attached: the ordinary cause is the unique index
    /// refusing a concurrent redelivery, which is correct behaviour and needs no action — and the
    /// rarer cause, a database that has gone away, is then visible with its own message rather than
    /// swallowed.
    /// </remarks>
    private static readonly Action<ILogger, Exception?> LogEpisodeNotStored =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(BruteForceDetectedHandler)),
            "The ban was placed on the host but its episode was not stored; the address is banned "
            + "and this panel's record of it is whatever another delivery of the same detection wrote");

    /// <summary>The Firewall module's database context, which is the durable store of every ban.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The one place this module asks whether an address is exempt from an automatic ban.</summary>
    private readonly WhitelistGuard _guard;

    /// <summary>The agent, which owns the host's ban set.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Where a refusal is reported, since nothing here returns a result to a caller.</summary>
    private readonly ILogger<BruteForceDetectedHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="guard">The whitelist guard, asked before every automatic ban.</param>
    /// <param name="agent">The agent client that installs the ban.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where a refusal is reported.</param>
    public BruteForceDetectedHandler(
        FirewallDbContext dbContext,
        WhitelistGuard guard,
        IAgentFirewallClient agent,
        IClock clock,
        FirewallAuditJournal journal,
        ILogger<BruteForceDetectedHandler> logger)
    {
        _dbContext = dbContext;
        _guard = guard;
        _agent = agent;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Bans the address the detector reported, or records why it was not banned.</summary>
    /// <param name="message">The detection: an address, a count, and the window it was counted over.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task HandleAsync(BruteForceDetected message, CancellationToken cancellationToken)
    {
        if (!IpAddressNormalizer.TryNormalize(message.IpAddress, out var address))
        {
            LogUnusableAddress(_logger, null);
            return;
        }

        var subject = address.ToString();

        if (await _guard.ExemptsAsync(address, cancellationToken))
        {
            await _journal.RecordSystemAsync(
                AuditActions.BanSkippedWhitelisted, subject, succeeded: true, cancellationToken);

            return;
        }

        var alreadyHandled = await _dbContext.BanEpisodes.AnyAsync(
            episode => episode.IpAddress == subject && episode.WindowStart == message.WindowStart,
            cancellationToken);
        if (alreadyHandled)
        {
            return;
        }

        var now = _clock.UtcNow;
        var since = now - EscalationWindow;

        // Every episode inside the window counts, including one an administrator lifted early and
        // one an administrator placed by hand. Both are deliberate, and both are the same statement:
        // this address has already had to be blocked today. Filtering out the lifted ones would let
        // an address be unbanned and re-offend indefinitely without ever reaching the day rung;
        // filtering out the manual ones would forget the bans a person cared enough to place.
        var priorEpisodes = await _dbContext.BanEpisodes.CountAsync(
            episode => episode.IpAddress == subject && episode.BannedAt > since,
            cancellationToken);

        var ladderTtl = BanTtlPolicy.ForPriorEpisodes(priorEpisodes);

        // AsNoTracking on purpose: this path READS the standing episodes and writes none of them.
        // See the type's remarks — an automatic ban never edits a row another decision owns, which
        // is what keeps the outcome independent of which handler's SaveChangesAsync commits last.
        var standing = await _dbContext.BanEpisodes
            .AsNoTracking()
            .Where(episode => episode.IpAddress == subject && episode.LiftedAt == null)
            .ToListAsync(cancellationToken);

        var inForce = standing.Where(episode =>
        {
            return episode.IsInForce(now);
        }).ToList();

        var ttl = LongerOf(ladderTtl, inForce, now);

        var banned = await _agent.BanAsync(subject, ttl, cancellationToken);
        if (!banned.IsSuccess)
        {
            LogBanRefused(_logger, banned.Error!.Code, null);
            await _journal.RecordSystemAsync(
                AuditActions.AddressBanned, subject, succeeded: false, cancellationToken);

            return;
        }

        var expiresAt = ttl is null ? (DateTimeOffset?)null : now + ttl.Value;

        _dbContext.BanEpisodes.Add(new BanEpisode(
            Guid.NewGuid(),
            subject,
            BanReason.BruteForce,
            message.WindowStart,
            message.Failures,
            now,
            expiresAt));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The redelivery check above is a read followed by a write, so two concurrent deliveries
            // of one detection both pass it and the database's unique index on (IpAddress,
            // WindowStart) refuses the second — which is the index doing its job. Letting that
            // escape would break this class's one promise: an exception here returns the message to
            // the queue, and the retry climbs the escalation ladder in place of the attacker.
            // Nothing is journalled, because the delivery that won the race has already recorded the
            // ban that exists.
            LogEpisodeNotStored(_logger, exception);
            return;
        }

        await _journal.RecordSystemAsync(AuditActions.AddressBanned, subject, succeeded: true, cancellationToken);
    }

    /// <summary>
    /// The longer of the ladder's rung and whatever of this address's standing bans has furthest to
    /// run — the duration the host is actually asked for.
    /// </summary>
    /// <param name="ladderTtl">What the escalation ladder alone would ask for.</param>
    /// <param name="inForce">Every episode for this address that is still in force at <paramref name="now"/>.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>The duration to ban for, or <c>null</c> for a ban that lasts until somebody lifts it.</returns>
    /// <remarks>
    /// <para>
    /// <b>An automatic ban must never shorten one that is already standing.</b> The host's ban set
    /// is keyed by address, and a second <c>add element</c> REPLACES the element and its timeout —
    /// measured on nftables v1.0.9 and documented on the agent's <c>ban_address</c>, where it also
    /// records that permanent converts to timed. So a fifteen-minute rung landing on top of an
    /// operator's permanent ban used to hand the kernel a fifteen-minute element. The address that
    /// causes an automatic ban is the address knocking, which makes that reachable on purpose: the
    /// harder somebody attacked, the sooner the ban placed against them ended.
    /// </para>
    /// <para>
    /// <b>The longer of the two, and not a refusal.</b> Declining the automatic ban outright while a
    /// longer one stands is simpler and stops the shortening just as well, and it is rejected: an
    /// address banned for ten minutes by hand and then genuinely attacking would keep its ten
    /// minutes and the ladder would never reach it. Taking the longer is the only rule that is
    /// monotone in both directions — a standing ban is never cut short, and a real attack is still
    /// escalated.
    /// </para>
    /// <para>
    /// <b>Read-only, and that is load-bearing.</b> Raising the duration asked for costs nothing that
    /// a concurrent writer can undo, because the aggregate rule is a maximum: a shorter automatic
    /// episode committed after a longer manual one cannot lower the answer. Rescheduling the manual
    /// row instead would have made the outcome depend on commit order, on a table with no
    /// transaction and no concurrency token.
    /// </para>
    /// <para>
    /// <b>Only an episode still in force counts.</b> A lifted or expired one is history the ladder
    /// reads, not a ban the kernel holds; letting one of those decide would pin an address to a
    /// permanent ban an administrator had deliberately ended.
    /// </para>
    /// </remarks>
    private static TimeSpan? LongerOf(TimeSpan ladderTtl, IReadOnlyCollection<BanEpisode> inForce, DateTimeOffset now)
    {
        var longest = ladderTtl;

        foreach (var episode in inForce)
        {
            var remaining = episode.RemainingTtl(now);
            if (remaining is null)
            {
                // Nothing is longer than a ban with no end, so the answer is settled.
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
