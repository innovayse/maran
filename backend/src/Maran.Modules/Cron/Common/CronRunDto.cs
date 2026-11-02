using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Common;

/// <summary>One run of a cron entry started by hand, as the history shows it (issue #158).</summary>
/// <param name="Id">The run's identity.</param>
/// <param name="EntryId">The agent's identifier for the entry that ran.</param>
/// <param name="StartedAt">When the panel started the run.</param>
/// <param name="DurationMilliseconds">How long the run took, measured by the agent.</param>
/// <param name="ExitCode">The program's exit status, or null when a ceiling stopped it or it reported none.</param>
/// <param name="Limit">The ceiling that stopped the run, or null when it ended on its own.</param>
/// <param name="Outcome">How the run ended: succeeded, failed or stopped.</param>
/// <param name="Stdout">What the program wrote to standard output, at most 64 KiB.</param>
/// <param name="Stderr">What the program wrote to standard error, at most 64 KiB.</param>
public sealed record CronRunDto(
    Guid Id,
    string EntryId,
    DateTimeOffset StartedAt,
    long DurationMilliseconds,
    int? ExitCode,
    CronRunLimit? Limit,
    CronRunOutcome Outcome,
    string Stdout,
    string Stderr);
