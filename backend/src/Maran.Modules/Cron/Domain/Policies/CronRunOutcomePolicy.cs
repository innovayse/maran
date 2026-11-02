using System.Linq.Expressions;
using Maran.Modules.Cron.Domain.Entities;
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

    /// <summary>
    /// The filter selecting runs whose outcome is <paramref name="outcome"/>, written over the stored
    /// end so a database translates it; it restates <see cref="Of"/> and must agree with it.
    /// </summary>
    /// <param name="outcome">The outcome to select.</param>
    /// <returns>A predicate over a run's stored end.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An outcome this policy has no rule for.</exception>
    public static Expression<Func<CronRun, bool>> Matching(CronRunOutcome outcome)
    {
        return outcome switch
        {
            CronRunOutcome.Stopped => run => run.EndKind == CronRunEndKind.Stopped,
            CronRunOutcome.Succeeded => run => run.EndKind == CronRunEndKind.Exited && run.EndExitCode == 0,
            CronRunOutcome.Failed => run => run.EndKind == CronRunEndKind.WithoutStatus
                || (run.EndKind == CronRunEndKind.Exited && run.EndExitCode != 0),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown cron run outcome."),
        };
    }
}
