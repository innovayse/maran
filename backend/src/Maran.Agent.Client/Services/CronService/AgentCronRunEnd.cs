namespace Maran.Agent.Client.Services.CronService;

/// <summary>
/// How a cron entry run by hand ended. Exactly one of three cases, each its own type, so an exit
/// status never sits beside a ceiling that stopped the run and a ceiling never sits beside a status.
/// </summary>
/// <remarks>
/// <para>
/// The wire spells this as an optional <c>exit_code</c> beside a <c>limit</c> whose zero value means
/// "none", which allows a status together with a ceiling and neither at all. The client resolves the
/// pair once, here, and the rest of the panel never sees the two fields.
/// </para>
/// <para>
/// The constructor is private to this assembly, so <see cref="AgentCronRunExited"/>,
/// <see cref="AgentCronRunWithoutStatus"/> and <see cref="AgentCronRunStopped"/> are the whole set.
/// </para>
/// </remarks>
public abstract record AgentCronRunEnd
{
    /// <summary>Closes the hierarchy to the three cases this assembly declares.</summary>
    private protected AgentCronRunEnd()
    {
    }
}
