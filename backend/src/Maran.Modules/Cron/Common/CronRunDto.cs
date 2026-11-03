using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Common;

/// <summary>One run of a cron entry started by hand, as the history shows it (issue #158).</summary>
/// <param name="Id">The run's identity.</param>
/// <param name="EntryId">The agent's identifier for the entry that ran.</param>
/// <param name="StartedAt">When the panel started the run.</param>
/// <param name="DurationMilliseconds">How long the run took, measured by the agent.</param>
/// <param name="End">
/// How the run ended in detail: with a status, with none, or stopped by one of the agent's ceilings.
/// </param>
/// <param name="Outcome">The verdict on it for the history's badge and filter: succeeded, failed or stopped.</param>
/// <param name="OutcomeName">
/// The same outcome as an operator reads it, localized for the request's culture by
/// <c>CronRunDisplayNames</c>: the word the history's badge prints, so the SPA holds no copy of it.
/// </param>
/// <param name="Stdout">What the program wrote to standard output, at most 64 KiB.</param>
/// <param name="Stderr">What the program wrote to standard error, at most 64 KiB.</param>
public sealed record CronRunDto(
    Guid Id,
    string EntryId,
    DateTimeOffset StartedAt,
    long DurationMilliseconds,
    CronRunEndDto End,
    CronRunOutcome Outcome,
    string OutcomeName,
    string Stdout,
    string Stderr);
