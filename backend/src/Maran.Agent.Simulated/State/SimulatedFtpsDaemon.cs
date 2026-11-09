namespace Maran.Agent.Simulated.State;

/// <summary>The configuration and run state of the simulated FTPS daemon.</summary>
/// <param name="Running">Whether the daemon is up.</param>
/// <param name="Hostname">The hostname it serves; empty until it was first enabled.</param>
/// <param name="PassivePortMin">Lowest passive data port; zero until it was first enabled.</param>
/// <param name="PassivePortMax">Highest passive data port; zero until it was first enabled.</param>
/// <param name="PassiveAddress">The address advertised in the PASV reply; empty when none is written.</param>
/// <param name="MaxClients">The concurrent-session ceiling; zero until it was first enabled.</param>
public sealed record SimulatedFtpsDaemon(
    bool Running,
    string Hostname,
    uint PassivePortMin,
    uint PassivePortMax,
    string PassiveAddress,
    uint MaxClients)
{
    /// <summary>A daemon that was never configured: stopped, with no hostname and no range.</summary>
    public static readonly SimulatedFtpsDaemon Unconfigured = new(false, string.Empty, 0, 0, string.Empty, 0);
}
