namespace Maran.Agent.Client.Services.SystemService;

/// <summary>The distribution family the agent runs on, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>DistroFamily</c> so callers outside this project never hold a
/// generated protobuf type. It deliberately has no "unspecified" member: an agent that states no
/// family is refused at the handshake, so a caller never holds a family it must second-guess.
/// </remarks>
public enum AgentDistroFamily
{
    /// <summary>Debian and its derivatives (Ubuntu, Debian).</summary>
    Debian,

    /// <summary>Red Hat Enterprise Linux and its rebuilds (AlmaLinux, Rocky Linux).</summary>
    Rhel,
}
