namespace Maran.Modules.Cron.Domain.ValueObjects;

/// <summary>
/// How a run started by hand ended. Exactly one of three cases, each its own type, so a status never
/// sits beside a ceiling that stopped the run and a ceiling never sits beside a status.
/// </summary>
/// <remarks>
/// <para>
/// The run's row keeps this in the columns it already has — <c>ExitCode</c>, <c>Limit</c> — and the
/// entity restores the case from them: a ceiling means <see cref="CronRunStopped"/>, otherwise a
/// status means <see cref="CronRunExited"/>, otherwise <see cref="CronRunEndedWithoutStatus"/>. The
/// third case is told from the second by an absent status alone, which is why neither column carries
/// a placeholder.
/// </para>
/// <para>
/// The constructor is private to this assembly, so the three cases are the whole set.
/// </para>
/// </remarks>
public abstract record CronRunEnd
{
    /// <summary>Closes the hierarchy to the three cases this assembly declares.</summary>
    private protected CronRunEnd()
    {
    }
}
