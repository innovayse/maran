using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Domain.Policies;

/// <summary>Decides how a run ended from what the agent reported about it.</summary>
/// <remarks>
/// One rule, stated once: a ceiling outranks a status, because a stopped run has none of its own; and
/// a run that ended with no status at all is a failure rather than a success, because only a 0 the
/// program itself returned says it worked.
/// </remarks>
public static class CronRunOutcomePolicy
{
    /// <summary>The outcome of a run that exited with <paramref name="exitCode"/> or hit <paramref name="limit"/>.</summary>
    /// <param name="exitCode">The program's status, or null when it reported none.</param>
    /// <param name="limit">The ceiling that stopped it, or null when it ended on its own.</param>
    /// <returns>The outcome the history shows.</returns>
    public static CronRunOutcome Of(int? exitCode, CronRunLimit? limit)
    {
        if (limit is not null)
        {
            return CronRunOutcome.Stopped;
        }

        return exitCode == 0 ? CronRunOutcome.Succeeded : CronRunOutcome.Failed;
    }
}
