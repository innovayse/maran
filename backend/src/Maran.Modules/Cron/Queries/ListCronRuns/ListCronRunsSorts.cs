using Maran.Modules.Cron.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Cron.Queries.ListCronRuns;

/// <summary>The sort whitelist of an entry's run history.</summary>
public static class ListCronRunsSorts
{
    /// <summary>Newest first by default; the id breaks ties so a page boundary never repeats a row.</summary>
    public static readonly SortMap<CronRun> Map = new SortMapBuilder<CronRun>()
        .By(CronRunSortFields.StartedAt, run => run.StartedAt)
        .By(CronRunSortFields.Duration, run => run.DurationMilliseconds)
        .DefaultTo(CronRunSortFields.StartedAt, SortDirection.Descending)
        .TieBreaker(run => run.Id);
}
