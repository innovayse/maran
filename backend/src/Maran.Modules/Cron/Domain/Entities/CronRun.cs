using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;

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
/// <see cref="Outcome"/> is decided once, by <see cref="CronRunOutcomePolicy"/>, from the two fields it
/// summarises — stored so the history can filter on it, and unable to disagree with them because
/// nothing writes either afterwards.
/// </para>
/// <para>
/// The output is the customer's own text returned to its owner. It is never written to a log line or
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

    /// <summary>The program's exit status, or null when a ceiling stopped it or it reported none.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>The ceiling that stopped the run, or null when it ended on its own.</summary>
    public CronRunLimit? Limit { get; private set; }

    /// <summary>How the run ended, decided once from <see cref="ExitCode"/> and <see cref="Limit"/>.</summary>
    public CronRunOutcome Outcome { get; private set; }

    /// <summary>What the program wrote to standard output, as the agent returned it.</summary>
    public string Stdout { get; private set; }

    /// <summary>What the program wrote to standard error, as the agent returned it.</summary>
    public string Stderr { get; private set; }

    /// <summary>Records one run.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account whose entry ran.</param>
    /// <param name="entryId">The agent's identifier for the entry.</param>
    /// <param name="startedAt">When the panel started the run.</param>
    /// <param name="durationMilliseconds">How long the run took.</param>
    /// <param name="exitCode">The program's status, or null.</param>
    /// <param name="limit">The ceiling that stopped it, or null.</param>
    /// <param name="stdout">Its standard output.</param>
    /// <param name="stderr">Its standard error.</param>
    public CronRun(
        Guid id,
        Guid accountId,
        string entryId,
        DateTimeOffset startedAt,
        long durationMilliseconds,
        int? exitCode,
        CronRunLimit? limit,
        string stdout,
        string stderr)
    {
        Id = id;
        AccountId = accountId;
        EntryId = entryId;
        StartedAt = startedAt;
        DurationMilliseconds = durationMilliseconds;
        ExitCode = limit is null ? exitCode : null;
        Limit = limit;
        Outcome = CronRunOutcomePolicy.Of(ExitCode, limit);
        Stdout = stdout;
        Stderr = stderr;
    }

    /// <summary>For EF Core materialisation only.</summary>
    private CronRun()
    {
        EntryId = string.Empty;
        Stdout = string.Empty;
        Stderr = string.Empty;
    }
}
