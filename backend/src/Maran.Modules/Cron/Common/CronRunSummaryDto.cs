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
/// <param name="End">
/// How the run ended in detail: with a status, with none, or stopped by one of the agent's ceilings.
/// </param>
/// <param name="Outcome">The verdict on it for the history's badge and filter: succeeded, failed or stopped.</param>
public sealed record CronRunSummaryDto(
    Guid Id,
    string EntryId,
    DateTimeOffset StartedAt,
    long DurationMilliseconds,
    CronRunEndDto End,
    CronRunOutcome Outcome);
