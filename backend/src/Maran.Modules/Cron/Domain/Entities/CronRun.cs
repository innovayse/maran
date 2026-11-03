using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;
using Maran.Modules.Cron.Domain.ValueObjects;

namespace Maran.Modules.Cron.Domain.Entities;

/// <summary>
/// One run of a cron entry started by hand from the panel (issue #158): when it started, how long it
/// took, how it ended and what it printed.
/// </summary>
/// <remarks>
/// <para>
/// A record of a FACT the panel alone knows — it started this run — and not a copy of anything the
/// crontab holds. The entry itself is still read from the account's crontab every time; this row
/// names it by the agent's id and outlives nothing but itself (removed with the entry, with the
/// account, and beyond <see cref="CronRunRetention.RunsPerEntry"/>).
/// </para>
/// <para>
/// Immutable: a run that happened does not change, so the entity has no method that changes it, and
/// <see cref="Outcome"/> is decided from <see cref="End"/> by <see cref="CronRunOutcomePolicy"/>, and
/// the history filters on it through <see cref="CronRunOutcomePolicy.Matching"/> over the stored end,
/// so there is one state and nothing to disagree with it.
/// </para>
/// <para>
/// Expand phase of the one-state change: the case is stored as <see cref="EndKind"/> plus the one
/// column that applies. The three older columns (<c>ExitCode</c>, <c>Limit</c>, <c>Outcome</c>) stay
/// mapped and are written by the constructor from the same end, so the previous release reading them
/// keeps working; nothing in this release reads them. A later release drops them.
/// </para>
/// <para>
/// The output is stored without NUL characters (<see cref="CronRunOutputPolicy"/>), which a text column
/// cannot hold. It is the customer's own text returned to its owner. It is never written to a log line or
/// to the audit journal (<c>CronAuditJournal</c>).
/// </para>
/// </remarks>
public sealed class CronRun
{
    /// <summary>The longest an agent entry id can be: a canonical uuid.</summary>
    public const int EntryIdMaxLength = 36;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account whose entry ran; the tenant column the query filter closes over.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The agent's identifier for the entry that ran.</summary>
    public string EntryId { get; private set; }

    /// <summary>When the panel asked the agent to start the run, by the panel's clock.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>How long the run took, measured by the agent, in milliseconds.</summary>
    public long DurationMilliseconds { get; private set; }

    /// <summary>The stored case of <see cref="End"/>; decides which of the two columns below carries a value.</summary>
    public CronRunEndKind EndKind { get; private set; }

    /// <summary>
    /// The program's exit status: set only when <see cref="EndKind"/> is <see cref="CronRunEndKind.Exited"/>,
    /// null otherwise.
    /// </summary>
    public int? EndExitCode { get; private set; }

    /// <summary>
    /// The ceiling that stopped the run: set only when <see cref="EndKind"/> is <see cref="CronRunEndKind.Stopped"/>,
    /// null otherwise.
    /// </summary>
    public CronRunLimit? EndLimit { get; private set; }

    /// <summary>How the run ended, decided from <see cref="End"/> by <see cref="CronRunOutcomePolicy"/>; not a column.</summary>
    public CronRunOutcome Outcome
    {
        get { return CronRunOutcomePolicy.Of(End); }
    }

    /// <summary>What the program wrote to standard output, as the agent returned it.</summary>
    public string Stdout { get; private set; }

    /// <summary>What the program wrote to standard error, as the agent returned it.</summary>
    public string Stderr { get; private set; }

    /// <summary>How the run ended, restored from <see cref="EndKind"/> and the column its case carries; not a column.</summary>
    public CronRunEnd End
    {
        get
        {
            return EndKind switch
            {
                CronRunEndKind.Stopped => new CronRunStopped(EndLimit!.Value),
                CronRunEndKind.Exited => new CronRunExited(EndExitCode!.Value),
                CronRunEndKind.WithoutStatus => new CronRunEndedWithoutStatus(),
                _ => throw new InvalidOperationException($"Unknown cron run end kind {EndKind}."),
            };
        }
    }

    /// <summary>The legacy <c>ExitCode</c> column, kept in step with <see cref="EndExitCode"/> until a later release.</summary>
    internal int? LegacyExitCode { get; private set; }

    /// <summary>The legacy <c>Limit</c> column, kept in step with <see cref="EndLimit"/> until a later release.</summary>
    internal CronRunLimit? LegacyLimit { get; private set; }

    /// <summary>The legacy <c>Outcome</c> column, kept in step with <see cref="Outcome"/> until a later release.</summary>
    internal CronRunOutcome LegacyOutcome { get; private set; }

    /// <summary>Records one run.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account whose entry ran.</param>
    /// <param name="entryId">The agent's identifier for the entry.</param>
    /// <param name="startedAt">When the panel started the run.</param>
    /// <param name="durationMilliseconds">How long the run took.</param>
    /// <param name="end">How the run ended.</param>
    /// <param name="stdout">Its standard output.</param>
    /// <param name="stderr">Its standard error.</param>
    public CronRun(
        Guid id,
        Guid accountId,
        string entryId,
        DateTimeOffset startedAt,
        long durationMilliseconds,
        CronRunEnd end,
        string stdout,
        string stderr)
    {
        Id = id;
        AccountId = accountId;
        EntryId = entryId;
        StartedAt = startedAt;
        DurationMilliseconds = durationMilliseconds;
        EndKind = end switch
        {
            CronRunExited => CronRunEndKind.Exited,
            CronRunStopped => CronRunEndKind.Stopped,
            CronRunEndedWithoutStatus => CronRunEndKind.WithoutStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(end), end, "Unknown cron run ending."),
        };
        EndExitCode = (end as CronRunExited)?.ExitCode;
        EndLimit = (end as CronRunStopped)?.Limit;
        LegacyExitCode = EndExitCode;
        LegacyLimit = EndLimit;
        LegacyOutcome = CronRunOutcomePolicy.Of(end);
        Stdout = CronRunOutputPolicy.Storable(stdout);
        Stderr = CronRunOutputPolicy.Storable(stderr);
    }

    /// <summary>For EF Core materialisation only.</summary>
    private CronRun()
    {
        EntryId = string.Empty;
        Stdout = string.Empty;
        Stderr = string.Empty;
    }
}
