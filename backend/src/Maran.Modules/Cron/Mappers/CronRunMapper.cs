using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;

namespace Maran.Modules.Cron.Mappers;

/// <summary>Restates a <see cref="CronRun"/> on the wire. Decides nothing.</summary>
public static class CronRunMapper
{
    /// <summary>The wire shape of <paramref name="run"/>.</summary>
    /// <param name="run">The recorded run.</param>
    /// <returns>Its DTO.</returns>
    public static CronRunDto From(CronRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new CronRunDto(
            run.Id,
            run.EntryId,
            run.StartedAt,
            run.DurationMilliseconds,
            run.ExitCode,
            run.Limit,
            run.Outcome,
            run.Stdout,
            run.Stderr);
    }

    /// <summary>The history row of <paramref name="run"/>: everything but its output.</summary>
    /// <param name="run">The recorded run.</param>
    /// <returns>Its summary DTO.</returns>
    public static CronRunSummaryDto Summary(CronRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new CronRunSummaryDto(
            run.Id,
            run.EntryId,
            run.StartedAt,
            run.DurationMilliseconds,
            run.ExitCode,
            run.Limit,
            run.Outcome);
    }
}
