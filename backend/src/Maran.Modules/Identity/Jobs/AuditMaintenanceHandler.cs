using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Identity.Jobs;

/// <summary>
/// Runs the audit journal's recurring upkeep: every tick it seals new rows into the hash chain, and
/// once a day it applies retention, pseudonymises the rows of actors that no longer exist, and verifies
/// the chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is the point.</b> Sealing comes first because retention purges only sealed rows and
/// verification can only read them. Retention comes before verification so the check reads the chain as
/// it stands after the purge, from the anchor the purge just advanced.
/// </para>
/// <para>
/// <b>One step failing never stops the others, and nothing here stops the journal.</b> Each step is
/// guarded: an exception is logged at Error with no row content and the next step still runs. A
/// retention run that fails is JOURNALLED as failed, because "no row" has to keep meaning "no run"
/// (threat note, section 2); a sweep that throws is journalled the same way. The one read that is not
/// guarded is the daily-schedule lookup at the top: if the database cannot answer it, nothing else
/// could run either, and the failure belongs to the message, where it is visible and retriable.
/// </para>
/// <para>
/// <b>The daily gate is stored, not remembered.</b> <see cref="AuditRetentionPolicy.LastRunAt"/> holds
/// the last daily run, so a restart neither skips a day nor repeats one. The time is recorded after
/// every attempt, including one where a step failed: an hourly retry of a step that fails would write
/// a failed entry an hour for as long as it fails, and the next day's run is the retry.
/// </para>
/// <para>
/// <b>Every daily run is journalled</b>, whether or not retention is on and whether or not it deleted
/// anything: <see cref="IdentityAuditActions.AuditRetentionRun"/> states the window, the cutoff, how
/// many rows were purged and through which chain index, and the actor is the panel itself.
/// </para>
/// <para>
/// <b>Stated limit.</b> A broken chain is written to the journal as a failed
/// <see cref="IdentityAuditActions.AuditChainVerified"/> entry and to the log; it is not mailed to
/// anyone. It is silent to everyone who reads neither.
/// </para>
/// </remarks>
public sealed class AuditMaintenanceHandler
{
    /// <summary>How long after a daily run the next one is due.</summary>
    public static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);

    /// <summary>What the erasure entry names as its subject when the orphan-actor sweep threw.</summary>
    private const string OrphanSweepFailedSubject = "orphaned-actor sweep failed";

    /// <summary>Pre-compiled log delegate for a step that threw.</summary>
    private static readonly Action<ILogger, string, Exception?> LogStepFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(AuditMaintenanceHandler)),
            "Audit maintenance step {Step} failed; the remaining steps still run");

    /// <summary>Pre-compiled log delegate for a chain that did not verify.</summary>
    private static readonly Action<ILogger, AuditChainBreak, long?, Exception?> LogChainBroken =
        LoggerMessage.Define<AuditChainBreak, long?>(
            LogLevel.Error,
            new EventId(2, nameof(AuditMaintenanceHandler)),
            "The audit journal's hash chain did not verify: {Break} at chain index {ChainIndex}");

    /// <summary>The module's database context, which owns the policy row.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Seals new rows into the chain.</summary>
    private readonly AuditChainSealer _sealer;

    /// <summary>Deletes the journal's old end.</summary>
    private readonly AuditRetentionRunner _retention;

    /// <summary>Pseudonymises the rows of actors that no longer exist.</summary>
    private readonly AuditPseudonymiser _pseudonymiser;

    /// <summary>Checks the chain.</summary>
    private readonly AuditChainVerifier _verifier;

    /// <summary>Writes the run's entries.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failing step is reported.</summary>
    private readonly ILogger<AuditMaintenanceHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sealer">Seals new rows into the chain.</param>
    /// <param name="retention">Deletes the journal's old end.</param>
    /// <param name="pseudonymiser">Pseudonymises the rows of actors that no longer exist.</param>
    /// <param name="verifier">Checks the chain.</param>
    /// <param name="journal">Writes the run's entries.</param>
    /// <param name="clock">The time source the daily gate is measured against.</param>
    /// <param name="logger">Where a failing step is reported.</param>
    public AuditMaintenanceHandler(
        IdentityDbContext dbContext,
        AuditChainSealer sealer,
        AuditRetentionRunner retention,
        AuditPseudonymiser pseudonymiser,
        AuditChainVerifier verifier,
        IdentityAuditJournal journal,
        IClock clock,
        ILogger<AuditMaintenanceHandler> logger)
    {
        _dbContext = dbContext;
        _sealer = sealer;
        _retention = retention;
        _pseudonymiser = pseudonymiser;
        _verifier = verifier;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one maintenance tick.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the tick between steps.</param>
    /// <returns>Resolves once the tick has finished.</returns>
    public async Task HandleAsync(AuditMaintenanceRequested message, CancellationToken cancellationToken)
    {
        await GuardedAsync(
            "seal",
            async () =>
            {
                await _sealer.SealAsync(AuditChainSealer.MaxBatchesPerRun, cancellationToken);
            },
            cancellationToken);

        var schedule = await _dbContext.AuditRetentionPolicies
            .AsNoTracking()
            .Where(row => row.Id == AuditRetentionPolicy.SingletonId)
            .Select(row => new { row.RetentionDays, row.LastRunAt })
            .FirstOrDefaultAsync(cancellationToken);

        var lastRunAt = schedule?.LastRunAt;

        if (lastRunAt is not null && _clock.UtcNow - lastRunAt.Value < DailyInterval)
        {
            return;
        }

        var retentionDays = schedule is null ? AuditRetentionPolicy.DefaultRetentionDays : schedule.RetentionDays;

        await RunRetentionAsync(retentionDays, cancellationToken);
        await SweepOrphanedActorsAsync(cancellationToken);
        await VerifyChainAsync(cancellationToken);
        await GuardedAsync("record-run", () =>
        {
            return RecordRunAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Applies retention and journals the run, failed or not.</summary>
    /// <param name="retentionDays">The window the run is expected to apply, for the entry's subject.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Resolves once the step and its entry are done.</returns>
    private async Task RunRetentionAsync(int? retentionDays, CancellationToken cancellationToken)
    {
        AuditRetentionOutcome? outcome = null;

        var finished = await GuardedAsync(
            "retention",
            async () =>
            {
                outcome = await _retention.RunAsync(cancellationToken);
            },
            cancellationToken);

        if (!finished || outcome is null)
        {
            await JournalAsync(
                IdentityAuditActions.AuditRetentionRun,
                AuditRetentionSubjectText.Failed(retentionDays),
                succeeded: false,
                cancellationToken);

            return;
        }

        await JournalAsync(
            IdentityAuditActions.AuditRetentionRun,
            AuditRetentionSubjectText.Run(retentionDays, outcome.Cutoff, outcome.Purged, outcome.ThroughChainIndex),
            succeeded: true,
            cancellationToken);
    }

    /// <summary>Pseudonymises the rows of orphaned actors, journalling only a sweep that threw.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Resolves once the step is done.</returns>
    /// <remarks>
    /// The sweep writes its own summary row, for success and for a refusal alike, so this step adds one
    /// only for the case the sweep cannot report itself: that it threw before it could.
    /// </remarks>
    private async Task SweepOrphanedActorsAsync(CancellationToken cancellationToken)
    {
        var finished = await GuardedAsync(
            "orphan-sweep",
            async () =>
            {
                await _pseudonymiser.PseudonymiseOrphanedActorsAsync(cancellationToken);
            },
            cancellationToken);

        if (!finished)
        {
            await JournalAsync(
                IdentityAuditActions.PersonalDataErased,
                OrphanSweepFailedSubject,
                succeeded: false,
                cancellationToken);
        }
    }

    /// <summary>Verifies the chain and journals the verdict, failed when a break was found.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Resolves once the step and its entry are done.</returns>
    private async Task VerifyChainAsync(CancellationToken cancellationToken)
    {
        await GuardedAsync(
            "verify",
            async () =>
            {
                var verdict = await _verifier.VerifyAsync(cancellationToken);

                if (!verdict.Intact)
                {
                    LogChainBroken(_logger, verdict.Break, verdict.FirstBrokenChainIndex, null);
                }

                await JournalAsync(
                    IdentityAuditActions.AuditChainVerified,
                    VerdictSubject(verdict),
                    verdict.Intact,
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Stamps the daily gate, creating the policy row at its default if none exists yet.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the time is stored.</returns>
    private async Task RecordRunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var policy = await _dbContext.AuditRetentionPolicies
            .FirstOrDefaultAsync(row => row.Id == AuditRetentionPolicy.SingletonId, cancellationToken);

        if (policy is null)
        {
            policy = new AuditRetentionPolicy(AuditRetentionPolicy.DefaultRetentionDays, now);
            _dbContext.AuditRetentionPolicies.Add(policy);
        }

        policy.RecordRun(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Spells a verdict for the journal: the kind of break and the first broken index, or "intact".</summary>
    /// <param name="verdict">What the verifier found.</param>
    /// <returns>The subject text; numbers and the break's name only, never a row's content.</returns>
    /// <remarks>
    /// A break at index 1 over the whole chain reads, to a person, as a changed panel key rather than
    /// as tampering; the verifier cannot tell the two apart and neither can this text.
    /// </remarks>
    private static string VerdictSubject(AuditChainVerdict verdict)
    {
        if (verdict.Intact)
        {
            return FormattableString.Invariant($"intact rowsChecked={verdict.RowsChecked}");
        }

        return FormattableString.Invariant(
            $"broken break={verdict.Break} firstBrokenChainIndex={verdict.FirstBrokenChainIndex} rowsChecked={verdict.RowsChecked}");
    }

    /// <summary>Writes one entry as the panel, so that a failing journal never fails the tick.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">The entry's subject.</param>
    /// <param name="succeeded">Whether the step it records succeeded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the entry is stored or its failure is logged.</returns>
    private async Task JournalAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await GuardedAsync(
            "journal",
            () =>
            {
                return _journal.RecordSystemAsync(action, subject, succeeded, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Runs one step, turning an exception into a log line so the next step still runs.</summary>
    /// <param name="step">The step's name, for the log.</param>
    /// <param name="work">The step.</param>
    /// <param name="cancellationToken">The tick's token; cancelling it is not a step failure.</param>
    /// <returns><c>true</c> when the step finished, <c>false</c> when it threw.</returns>
    private async Task<bool> GuardedAsync(string step, Func<Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await work();

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogStepFailed(_logger, step, exception);

            // A step that failed in SaveChanges (a seal that lost the race on the unique chain index, a
            // purge that was refused) leaves its rejected changes tracked on this scoped context. The
            // next step's save would re-send them and fail the same way, so the journal entry that
            // reports the failure could never be written. Nothing a failed step staged is wanted.
            _dbContext.ChangeTracker.Clear();

            return false;
        }
    }
}
