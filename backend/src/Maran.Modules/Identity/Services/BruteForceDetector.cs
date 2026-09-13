using System.Net;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Counts refused sign-ins per source address and announces <see cref="BruteForceDetected"/> when an
/// address crosses the panel's threshold. The producer half of the ban path; the Firewall module
/// owns the other half and everything that happens after the announcement.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this type deliberately does NOT do.</b> It does not decide how long a ban lasts, it does
/// not escalate a repeat offender, and it does not consult the whitelist. All three belong to the
/// subscriber, which owns the host's ban set and the record of who has been banned before — and
/// duplicating any of them here would give this module a vote in the other's decision and two places
/// for the answer to be changed. Its whole output is "this address failed this many times, in a
/// window that started here".
/// </para>
/// <para>
/// <b>Where the count lives, and why it is a row.</b> See <see cref="FailedLoginByIp"/>. In short:
/// it has to survive the requests it spans, it must not grow without bound while the panel is under
/// attack, and it must be cheap enough that a login flood is not a denial of service in its own
/// right. A row answers all three — durable across restarts, one row per address rather than one per
/// attempt, reclaimed once its window closes, and one indexed statement beside an Argon2id
/// verification that already dominates the request by orders of magnitude.
/// </para>
/// <para>
/// <b>The announcement is PUBLISHED, never invoked.</b> Invoking it inline would put the agent's ban
/// call — a round trip to a separate root process over a unix socket, with a timeout — inside the
/// request of the attacker who triggered it, which hands them a way to hold a request thread. It
/// would also mean a subscriber's failure surfacing to them as a 500 where every other refused
/// sign-in is a 401. Published, the login answers immediately and the ban is installed behind it.
/// </para>
/// <para>
/// <b>What a restart does.</b> Nothing to the count: the window is a row in PostgreSQL, so a panel
/// restarted mid-attack resumes at the number the address had reached rather than handing it a free
/// reset. What a restart CAN lose is a detection already published and not yet handled — the local
/// queue is in memory — and the cost of that is one wave: the address has to earn the threshold
/// again. It is the right thing to lose, because the bans themselves are durable on the other side
/// and are re-applied at startup by the Firewall module's reconciler.
/// </para>
/// <para>
/// <b>What concurrency does, stated rather than assumed.</b> The count is read, incremented and
/// written, so two refused sign-ins from one address landing in overlapping requests can advance it
/// by one instead of two. It undercounts, never over — an attacker cannot manufacture a ban for
/// somebody else this way — and the number of attempts that can overlap from one address is bounded
/// by the login rate limiter, whose queue limit is zero. The residual is named in this task's threat
/// note rather than papered over.
/// </para>
/// </remarks>
public sealed class BruteForceDetector
{
    /// <summary>
    /// How many closed windows one refused sign-in reclaims.
    /// </summary>
    /// <remarks>
    /// Larger than one on purpose, and that is the whole argument: each refused sign-in can create at
    /// most ONE row and removes up to this many, so the table drains strictly faster than the load
    /// that fills it. The bound on its size is therefore "one row per address that failed a sign-in
    /// inside the window", which is the number of addresses currently attacking rather than the
    /// number ever seen. Small enough that the sweep stays an index range scan of a few rows.
    /// </remarks>
    private const int ReclaimBatchSize = 64;

    /// <summary>Pre-compiled log delegate for a failure that raced another and was not counted.</summary>
    /// <remarks>
    /// The address is deliberately not a parameter. It is attacker-supplied text on the one path
    /// this line is written from, and the operator-facing fact is that the counter lost a race, not
    /// which address it was: the row that won the race carries the address, and the audit journal
    /// carries every attempt.
    /// </remarks>
    private static readonly Action<ILogger, Exception?> LogFailureNotCounted =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(BruteForceDetector)),
            "A refused sign-in was not added to its address's brute-force count: a concurrent attempt "
            + "from the same address wrote the row first. The count is one lower than the attempts.");

    /// <summary>The module's database context, which holds the counting windows.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The bus a crossing is announced on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>How many failures in how long count as an attack.</summary>
    private readonly BruteForceOptions _options;

    /// <summary>Where a lost count is reported, since nothing here returns a result to a caller.</summary>
    private readonly ILogger<BruteForceDetector> _logger;

    /// <summary>Creates the detector.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="bus">The bus <see cref="BruteForceDetected"/> is published on.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="options">The module's brute-force policy.</param>
    /// <param name="logger">Where a lost count is reported.</param>
    public BruteForceDetector(
        IdentityDbContext dbContext,
        IMessageBus bus,
        IClock clock,
        IOptions<BruteForceOptions> options,
        ILogger<BruteForceDetector> logger)
    {
        _dbContext = dbContext;
        _bus = bus;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Counts one refused sign-in against its source address, and announces an attack.</summary>
    /// <param name="ipAddress">
    /// The caller's address as <c>ClientAddress</c> rendered it — the real client's, because
    /// forwarded headers are honoured from the local reverse proxy before any controller reads it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the count is stored and any detection has been handed to the bus.</returns>
    public async Task RecordFailureAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(ipAddress, out _))
        {
            // ClientAddress.Unknown, and nothing else, reaches this. Counting under it would pool
            // every peer-less request into one bucket that names nobody, and announcing that bucket
            // would ask the firewall to ban a word.
            return;
        }

        var now = _clock.UtcNow;
        var window = _options.Window;

        var record = await _dbContext.FailedLoginsByIp
            .SingleOrDefaultAsync(f => f.IpAddress == ipAddress, cancellationToken);

        if (record is null)
        {
            record = new FailedLoginByIp(ipAddress, now);
            _dbContext.FailedLoginsByIp.Add(record);
        }
        else
        {
            record.RecordFailure(now, window);
        }

        // Captured BEFORE the window is removed below, because the announcement describes the window
        // that crossed the threshold.
        var detection = record.HasReached(_options.MaxFailuresPerAddress)
            ? new BruteForceDetected(ipAddress, record.Failures, record.WindowStart)
            : null;

        if (detection is not null)
        {
            // The window is DELETED rather than zeroed, and the difference is load-bearing. The
            // subscriber's idempotency key is (address, window start), so the next window must start
            // at the instant of a real future failure — not at this instant, which is when the window
            // just announced ENDED. Zeroing in place would give the address's next episode the same
            // key as this one, and the subscriber would correctly refuse it as a redelivery: the
            // second wave would be a ban that never happened.
            //
            // Deleting is also what stops the twenty-sixth failure from being a second attack and
            // the twenty-seventh a third, which would climb the fifteen-minute/hour/day ladder in
            // three extra attempts rather than in three separate waves.
            _dbContext.FailedLoginsByIp.Remove(record);
        }

        await ReclaimClosedWindowsAsync(ipAddress, now - window, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The insert lost a race with a concurrent refused sign-in from the same address, whose
            // row now holds the primary key. Not swallowed: it is logged, and it is a known outcome
            // of this design rather than a fault — the other request counted the attempt this one
            // could not. Letting it escape would answer a refused sign-in with a 500 where every
            // other refusal is a 401, which is both a worse answer and a distinguishable one.
            LogFailureNotCounted(_logger, null);
            return;
        }

        if (detection is not null)
        {
            // Published after the window's removal is committed. A detection is lost only if the
            // process dies between the two, and that costs nothing an attacker can use: the address
            // simply has to earn the threshold again from nothing.
            await _bus.PublishAsync(detection);
        }
    }

    /// <summary>Removes a batch of windows that closed before <paramref name="cutoff"/>.</summary>
    /// <param name="keep">The address being counted right now, whose row must survive this sweep.</param>
    /// <param name="cutoff">The instant before which a window is over and its row is dead weight.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the batch is marked for deletion; nothing is written until the caller saves.</returns>
    /// <remarks>
    /// <para>
    /// Bounded work, deliberately: <see cref="ReclaimBatchSize"/> rows at most, from an index range
    /// scan, and usually none at all. An unbounded delete would make the cost of one refused sign-in
    /// depend on how many addresses had attacked in the previous window — which is precisely the
    /// moment the panel can least afford it.
    /// </para>
    /// <para>
    /// The row being counted is excluded rather than being allowed to fall out of the range, because
    /// it has just been given a fresh <c>WindowStart</c> and deleting an entity the same
    /// <c>SaveChanges</c> is inserting or updating is a conflict, not a tidy-up.
    /// </para>
    /// </remarks>
    private async Task ReclaimClosedWindowsAsync(
        string keep,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var closed = await _dbContext.FailedLoginsByIp
            .Where(f => f.IpAddress != keep && f.WindowStart < cutoff)
            .OrderBy(f => f.WindowStart)
            .Take(ReclaimBatchSize)
            .ToListAsync(cancellationToken);

        _dbContext.FailedLoginsByIp.RemoveRange(closed);
    }
}
