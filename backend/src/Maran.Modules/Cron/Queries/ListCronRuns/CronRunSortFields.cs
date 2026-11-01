namespace Maran.Modules.Cron.Queries.ListCronRuns;

/// <summary>The names an entry's run history may be sorted by, as the <c>sort</c> parameter spells them.</summary>
public static class CronRunSortFields
{
    /// <summary>When the run started; the default, newest first.</summary>
    public const string StartedAt = "startedAt";

    /// <summary>How long the run took.</summary>
    public const string Duration = "duration";
}
