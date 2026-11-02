namespace Maran.Modules.Cron.Domain.Enums;

/// <summary>
/// Which of the three ways a run started by hand ended, as the run's row stores it. The kind decides
/// which of the two optional columns beside it carries a value: a status for <see cref="Exited"/>, a
/// ceiling for <see cref="Stopped"/>, neither for <see cref="WithoutStatus"/>.
/// </summary>
public enum CronRunEndKind
{
    /// <summary>The program ended on its own and returned a status.</summary>
    Exited = 1,

    /// <summary>The program ended on its own but reported no status of its own (it died of a signal).</summary>
    WithoutStatus = 2,

    /// <summary>One of the agent's ceilings stopped the program before it ended.</summary>
    Stopped = 3,
}
