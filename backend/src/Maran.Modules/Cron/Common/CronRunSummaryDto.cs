using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Common;

/// <summary>
/// One row of an entry's run history: everything about a run started by hand except what it printed.
/// </summary>
/// <remarks>
/// The output is left out of the list on purpose. Each run may carry 128 KiB of it, and a page of a
/// hundred would be a response of megabytes that nobody reads row by row; one run's output is read
/// on its own (<see cref="CronRunDto"/>).
/// </remarks>
/// <param name="Id">The run's identity.</param>
/// <param name="EntryId">The agent's identifier for the entry that ran.</param>
/// <param name="StartedAt">When the panel started the run.</param>
/// <param name="DurationMilliseconds">How long the run took, measured by the agent.</param>
/// <param name="ExitCode">The program's exit status, or null when a ceiling stopped it or it reported none.</param>
/// <param name="Limit">The ceiling that stopped the run, or null when it ended on its own.</param>
/// <param name="Outcome">How the run ended: succeeded, failed or stopped.</param>
public sealed record CronRunSummaryDto(
    Guid Id,
    string EntryId,
    DateTimeOffset StartedAt,
    long DurationMilliseconds,
    int? ExitCode,
    CronRunLimit? Limit,
    CronRunOutcome Outcome);
