namespace Maran.Modules.Cron.Domain.ValueObjects;

/// <summary>
/// How a run started by hand ended. Exactly one of three cases, each its own type, so a status never
/// sits beside a ceiling that stopped the run and a ceiling never sits beside a status.
/// </summary>
/// <remarks>
/// <para>
/// The run's row keeps the case as a stored kind (<c>EndKind</c>) beside the one nullable column
/// that case carries — <c>EndExitCode</c> for <see cref="CronRunExited"/>, <c>EndLimit</c> for
/// <see cref="CronRunStopped"/>, neither for <see cref="CronRunEndedWithoutStatus"/> — and the entity
/// restores it from them, so no column carries a placeholder.
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
