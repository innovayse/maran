using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Closes every backup row that was still <see cref="BackupStatus.Running"/> when the panel last
/// stopped, by ASKING the agent what is on the destination rather than by guessing from its age.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> <c>CreateBackup</c> is the agent's one "run to completion and then
/// reclaim" rpc (rules/rust.md, "Async and blocking"): it runs on a detached blocking task, so a
/// panel that loses the stream stops nothing — the archive finishes and lands on disk. The
/// in-process half of that was already handled, by a <c>catch</c> in both create paths that marks
/// the row abandoned. What nothing did was look AFTERWARDS. A process killed or restarted
/// mid-stream left a <see cref="BackupStatus.Running"/> row that no code on earth would move, and
/// such a row is not inert: it refuses every restore of that account, refuses its own deletion
/// (<see cref="Backup.MayBeDeleted"/>), and is invisible to retention
/// (<see cref="Backup.MayBeRetentionPruned"/> admits completed rows only). One lost stream
/// therefore cost the customer the ability to restore from any of their GOOD backups, for ever.
/// </para>
/// <para>
/// <b>It observes rather than infers, and the observation is a real one.</b> The pass calls
/// <see cref="IAgentBackupClient.ListAsync"/> — declared in the contract, implemented end to end,
/// and until now called by nothing in the panel — and asks the destination what artifacts exist.
/// That is an observation of the artefact the restore path would actually read, on the path the
/// agent reads it from, which is what rules/testing.md asks a check to do. A
/// <c>readable</c> entry means a FINISHED archive and cannot mean a half-written one: the agent
/// publishes by rename after checksumming, and <c>ops::backup::list_backups</c> never lists a
/// <c>.partial</c> file, reading the directory itself rather than trusting that a killed run
/// cleaned up.
/// </para>
/// <para>
/// <b>What it does NOT do: complete the row.</b> An artifact is present, its size and its SHA-256
/// are right there in the listing, and writing them onto the row would produce exactly what the
/// panel would have recorded — and it is refused. <see cref="Backup.Sha256"/>'s own doc states why:
/// <i>"a digest read from beside the bytes it describes proves only that the two were written
/// together, which is exactly what an attacker who replaced both would arrange."</i> A row completed
/// from a sidecar would be indistinguishable from one the panel observed, and the restore path's
/// whole verification argument rests on that distinction. So the row is FAILED with a code that says
/// an archive exists and the panel cannot vouch for it, and the archive is reported and left where
/// it is. Nothing is destroyed: a failed row may be deleted by an operator, and retention — which
/// admits completed rows only — will never touch those bytes unattended.
/// </para>
/// <para>
/// <b>Where it still guesses, and how the guess is bounded.</b> One thing the panel cannot ask.
/// <c>AgentInfo</c> carries a version, a distro, a family, a proto revision and the backup root —
/// no start time and no boot id — so "there is no artifact yet" cannot be split into "the agent is
/// still writing it" and "the process that was writing it is gone". The residual guess is therefore
/// the one case of a row with no artifact whose run the agent is STILL driving right now. It is
/// bounded three ways. First, only rows started before <see cref="_processStartedAt"/> are
/// candidates at all, so a run this process began is never touched — the boundary
/// <c>StartupTaskReconciler</c> uses, for the same reason and read once for the same reason.
/// Second, the mistake destroys nothing: the row becomes failed, no artifact is deleted, and when
/// the archive does land the next pass reports it as present-and-unvouched instead of losing it.
/// Third, it is chosen in the direction where being wrong is recoverable — a customer can retake a
/// backup, and cannot un-stick a row nothing looks at. The opposite policy, sparing such rows,
/// spares dead ones for ever, which is the state this class exists to end.
/// </para>
/// <para>
/// <b>A row it cannot observe is LEFT ALONE.</b> If the destination will not resolve, or the agent
/// refuses the listing, the pass closes nothing and counts the row as unobservable. That is the
/// preference for observation taken seriously: a pass that closed rows when the agent was
/// unreachable would be inferring from silence, which is the one thing it was written not to do. The
/// cost is that such a row stays stuck until a start at which the agent answers, and the count says
/// so in the log.
/// </para>
/// <para>
/// <b>It reads with <c>IgnoreQueryFilters</c>, and must.</b> The module's filter admits
/// administrators and a hosted service has no signed-in caller at all, so the filtered read would
/// find nothing, every time, and the class would appear to work while doing nothing. It is safe
/// because nothing read here is returned to anybody: the rows are closed in place. Accounts are read
/// through <see cref="IAccountDirectory.ListAsync"/> for the same reason the nightly sweep does —
/// the scoped <c>FindAsync</c> would answer null for every account, for ever.
/// </para>
/// <para>
/// <b><see cref="Backup.FinishedAt"/> becomes the instant the panel NOTICED</b>, not the instant the
/// run ended, because nothing recorded the latter: the process that would have written it is what
/// stopped. The same trade <c>StartupTaskReconciler</c> makes and states.
/// </para>
/// <para>
/// A failed pass is retried a bounded number of times: at boot the database or the agent socket may
/// not be reachable yet, and a pass that has failed <see cref="MaximumAttempts"/> times is a broken
/// host rather than a slow start.
/// </para>
/// </remarks>
public sealed class StartupBackupReconciler : BackgroundService
{
    /// <summary>How many times a failed pass is retried before the reconciler gives up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, int, Exception?> LogReclaimed =
        LoggerMessage.Define<int, int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(StartupBackupReconciler)),
            "Examined {Examined} backups left running by the previous process: {ArtifactPresent} had "
            + "an archive on the destination the panel cannot vouch for, {ArtifactAbsent} had none, "
            + "and {Unobservable} could not be asked about and were left alone");

    /// <summary>Pre-compiled log delegate for a reclaimed row whose archive is still on the disk.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogArtifactLeftBehind =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2, nameof(StartupBackupReconciler)),
            "Backup {BackupId} was still running when the panel last stopped and its archive IS on "
            + "the destination, but the panel never recorded the digest, so it cannot be restored "
            + "from. The row is failed and the archive was NOT deleted: releasing those bytes is an "
            + "operator's decision");

    /// <summary>Pre-compiled log delegate for a row the pass could not ask the agent about.</summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogUnobservable =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(3, nameof(StartupBackupReconciler)),
            "Backup {BackupId} was left running by the previous process and its destination could "
            + "not be asked what it holds ({Code}), so the row was LEFT RUNNING rather than closed "
            + "on a guess. It blocks every restore of that account until a start at which the agent "
            + "answers");

    /// <summary>Pre-compiled log delegate for a pass that did not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(4, nameof(StartupBackupReconciler)),
            "Could not reclaim abandoned backups on attempt {Attempt}; the panel may be refusing "
            + "restores for an account whose backup row is stuck");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(5, nameof(StartupBackupReconciler)),
            "Gave up reclaiming abandoned backups after {Attempts} attempts; any row left running by "
            + "the previous process will keep refusing that account's restores until the panel is "
            + "restarted");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <see cref="BackupsDbContext"/>. A <see cref="BackgroundService"/> is a
    /// singleton, the context is scoped, and a singleton capturing a scoped dependency is refused by
    /// the container at BUILD time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The window onto the destination: what artifacts actually exist.</summary>
    /// <remarks>
    /// A singleton in this panel's composition, so it is injected directly rather than resolved per
    /// scope. It is the whole reason this class is an observation and not an inference.
    /// </remarks>
    private readonly IAgentBackupClient _agent;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<StartupBackupReconciler> _logger;

    /// <summary>
    /// When this process started, as the only boundary between a run that was abandoned and a run
    /// that is in flight right now. Read once, in the constructor, because the container builds
    /// hosted services before the first request is served — reading it per pass would move the
    /// boundary forward across a retry and let the second attempt close what the first spared.
    /// </summary>
    private readonly DateTimeOffset _processStartedAt;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="agent">The window onto what the destination actually holds.</param>
    /// <param name="clock">The panel's clock, which fixes the boundary and stamps the outcomes.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public StartupBackupReconciler(
        IServiceScopeFactory scopeFactory,
        IAgentBackupClient agent,
        IClock clock,
        ILogger<StartupBackupReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _scopeFactory = scopeFactory;
        _agent = agent;
        _clock = clock;
        _logger = logger;
        _processStartedAt = clock.UtcNow;
    }

    /// <summary>Reclaims every backup the previous process left running, once.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass examined and what it decided about each row.</returns>
    /// <remarks>
    /// Public because it is the pass, and the pass is what has behaviour worth asserting: a test
    /// drives it directly rather than starting a hosted service and waiting for a timer, which is
    /// the sleep rules/testing.md forbids.
    /// </remarks>
    public async Task<BackupReclamation> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var destinations = scope.ServiceProvider.GetRequiredService<BackupDestinationResolver>();
        var journal = scope.ServiceProvider.GetRequiredService<BackupAuditJournal>();

        // Resolved per pass and NOT captured in the constructor, and the difference is the whole
        // service: the Accounts module registers its directory SCOPED, and a singleton hosted
        // service that took it as a constructor dependency is refused by the container at BUILD
        // time — which stops the whole API rather than degrading this one pass. That is not a
        // hypothetical: this class was written that way first, and
        // BackgroundWorkRegistrationTests.The_backup_reclamation_pass_is_registered_as_a_hosted_service
        // failed with "Cannot consume scoped service 'IAccountDirectory' from singleton
        // 'IHostedService'" before the dependency moved here.
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountDirectory>();

        // Both halves of the predicate carry weight. The status is what makes a row a candidate; the
        // boundary is what makes the pass safe to run late, because a hosted service starts alongside
        // the web server rather than strictly before it, and a backup opened by the first request to
        // arrive can be in the table while this is reading it.
        var boundary = _processStartedAt;
#pragma warning disable RS0030 // reclamation runs at startup, before any request, and must see rows left by the dead process
        var abandoned = await dbContext.Backups
            .IgnoreQueryFilters()
            .Where(backup => backup.Status == BackupStatus.Running && backup.StartedAt < boundary)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        if (abandoned.Count == 0)
        {
            var nothing = new BackupReclamation(0, 0, 0, 0);
            LogReclaimed(_logger, 0, 0, 0, 0, null);
            return nothing;
        }

        var snapshots = (await accounts.ListAsync(cancellationToken))
            .ToDictionary(account => { return account.Id; });

        var present = 0;
        var absent = 0;
        var unobservable = 0;

        foreach (var backup in abandoned)
        {
            var observation = await ObserveAsync(backup, snapshots, destinations, cancellationToken);

            if (observation is null)
            {
                unobservable++;
                continue;
            }

            if (observation.Value)
            {
                present++;
                backup.Failed(nameof(ErrorMessages.BackupArtifactUnvouched), _clock.UtcNow);
                LogArtifactLeftBehind(_logger, backup.Id, null);
            }
            else
            {
                absent++;
                backup.Failed(nameof(ErrorMessages.BackupOutcomeUnobserved), _clock.UtcNow);
            }

            await journal.RecordScheduledAsync(
                AuditActions.BackupCreated,
                SubjectOf(backup, snapshots),
                succeeded: false,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var reclamation = new BackupReclamation(abandoned.Count, present, absent, unobservable);
        LogReclaimed(_logger, abandoned.Count, present, absent, unobservable, null);

        return reclamation;
    }

    /// <summary>What an operator searches the journal for: the account this row backs up.</summary>
    /// <param name="backup">The row being closed.</param>
    /// <param name="accounts">Every account on the host, by id.</param>
    /// <returns>The account's system user name, or the name the row kept when its account was deleted.</returns>
    /// <remarks>
    /// The row's own stamped name wins for an orphan, because after the account cascade there is
    /// nowhere else to read it from (<see cref="Backup.NamesADeletedAccount"/>); an account still
    /// present answers from the directory, which is the fact's owner. An empty string is the honest
    /// answer for a row whose account is gone and which was never stamped — such a row should not
    /// exist, and inventing a subject for it would put a guess in an append-only journal.
    /// </remarks>
    private static string SubjectOf(Backup backup, IReadOnlyDictionary<Guid, AccountSnapshot> accounts)
    {
        if (backup.NamesADeletedAccount())
        {
            return backup.OrphanedAccountUsername;
        }

        return accounts.TryGetValue(backup.AccountId, out var account) ? account.Username : string.Empty;
    }

    /// <summary>Asks the destination whether this row's archive is there.</summary>
    /// <param name="backup">The row left running by the previous process.</param>
    /// <param name="accounts">Every account on the host, by id.</param>
    /// <param name="destinations">Resolves the destination the row named.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// True when the destination holds a readable archive under this row's id, false when it holds
    /// none, and <c>null</c> when the question could not be asked at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Only a <c>Readable</c> entry counts as present.</b> An entry the agent lists as unreadable
    /// is an artifact it cannot describe — a missing or unparsable sidecar, a directory wearing an
    /// archive's name — and treating it as a finished backup would be the panel believing the one
    /// thing the agent explicitly declined to say. It is therefore reclaimed as absent, which fails
    /// the row and leaves the file alone for an operator.
    /// </para>
    /// <para>
    /// The three-valued answer is a <c>bool?</c> rather than two booleans because the caller must not
    /// be able to read "could not ask" as "not there": that conflation is what would turn this pass
    /// back into an inference from silence.
    /// </para>
    /// </remarks>
    private async Task<bool?> ObserveAsync(
        Backup backup,
        IReadOnlyDictionary<Guid, AccountSnapshot> accounts,
        BackupDestinationResolver destinations,
        CancellationToken cancellationToken)
    {
        var username = SubjectOf(backup, accounts);
        if (username.Length == 0)
        {
            LogUnobservable(_logger, backup.Id, nameof(ErrorMessages.AccountNotFound), null);
            return null;
        }

        var destination = await destinations.ResolveAsync(backup.DestinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            LogUnobservable(_logger, backup.Id, destination.Error!.Code, null);
            return null;
        }

        var listing = await _agent.ListAsync(username, destination.Value!.Agent, cancellationToken);
        if (!listing.IsSuccess)
        {
            LogUnobservable(_logger, backup.Id, listing.Error!.Code, null);
            return null;
        }

        var identifier = backup.Id.ToString();

        return listing.Value!.Any(entry =>
        {
            return entry.Readable is not null
                && string.Equals(entry.BackupId, identifier, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Runs passes until one succeeds or the attempts run out.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves when the pass has succeeded or the attempts are spent.</returns>
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
    /// <returns>True when the pass completed.</returns>
    /// <remarks>
    /// A database or an agent socket that is not reachable yet arrives here as an exception rather
    /// than as a failed <c>Result</c>. Letting it escape <see cref="ExecuteAsync"/> would stop the
    /// service for the lifetime of the process — the same outcome as never having written it, on the
    /// one boot where it mattered most.
    /// </remarks>
    private async Task<bool> AttemptAsync(int attempt, CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
            return true;
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
    }
}
