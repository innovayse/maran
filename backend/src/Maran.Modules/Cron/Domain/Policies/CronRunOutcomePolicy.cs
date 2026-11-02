using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.ValueObjects;

namespace Maran.Modules.Cron.Domain.Policies;

/// <summary>Decides how a run ended from what the agent reported about it.</summary>
/// <remarks>
/// One rule, stated once: a ceiling outranks a status, because a stopped run has none of its own; and
/// a run that ended with no status at all is a failure rather than a success, because only a 0 the
/// program itself returned says it worked.
/// </remarks>
public static class CronRunOutcomePolicy
{
    /// <summary>The outcome of a run that ended as <paramref name="end"/> says.</summary>
    /// <param name="end">How the run ended.</param>
    /// <returns>The outcome the history shows.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An ending this policy has no rule for.</exception>
    public static CronRunOutcome Of(CronRunEnd end)
    {
        return end switch
        {
            CronRunStopped => CronRunOutcome.Stopped,
            CronRunExited { ExitCode: 0 } => CronRunOutcome.Succeeded,
            CronRunExited => CronRunOutcome.Failed,
            CronRunEndedWithoutStatus => CronRunOutcome.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(end), end, "Unknown cron run ending."),
        };
    }
}
