using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.ValueObjects;

namespace Maran.Modules.Cron.Mappers;

/// <summary>Restates a <see cref="CronRun"/> on the wire. Decides nothing.</summary>
public static class CronRunMapper
{
    /// <summary>The wire shape of <paramref name="run"/>.</summary>
    /// <param name="run">The recorded run.</param>
    /// <param name="outcomeName">
    /// The run's outcome as an operator reads it, already resolved for the request's culture by
    /// <c>CronRunDisplayNames</c>. Passed in because a mapper translates and never decides
    /// (rules/csharp.md), and a resource lookup is a decision about culture.
    /// </param>
    /// <returns>Its DTO.</returns>
    public static CronRunDto From(CronRun run, string outcomeName)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new CronRunDto(
            run.Id,
            run.EntryId,
            run.StartedAt,
            run.DurationMilliseconds,
            End(run.End),
            run.Outcome,
            outcomeName,
            run.Stdout,
            run.Stderr);
    }

    /// <summary>The history row of <paramref name="run"/>: everything but its output.</summary>
    /// <param name="run">The recorded run.</param>
    /// <param name="outcomeName">The run's outcome as an operator reads it; see <see cref="From"/>.</param>
    /// <returns>Its summary DTO.</returns>
    public static CronRunSummaryDto Summary(CronRun run, string outcomeName)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new CronRunSummaryDto(
            run.Id,
            run.EntryId,
            run.StartedAt,
            run.DurationMilliseconds,
            End(run.End),
            run.Outcome,
            outcomeName);
    }

    /// <summary>The wire shape of how a run ended.</summary>
    /// <param name="end">How the run ended.</param>
    /// <returns>Its DTO, one shape per case.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An ending this mapper has no shape for.</exception>
    private static CronRunEndDto End(CronRunEnd end)
    {
        return end switch
        {
            CronRunExited exited => new CronRunExitedDto(exited.ExitCode),
            CronRunEndedWithoutStatus => new CronRunWithoutStatusDto(),
            CronRunStopped stopped => new CronRunStoppedDto(stopped.Limit),
            _ => throw new ArgumentOutOfRangeException(nameof(end), end, "Unknown cron run ending."),
        };
    }
}
