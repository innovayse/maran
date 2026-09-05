namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>The transport protocol a port rule applies to, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>Protocol</c> so callers outside this project never hold a
/// generated protobuf type. It deliberately has no "unspecified" member: the agent refuses that
/// value, so a caller must not be able to express it.
/// </remarks>
public enum AgentFirewallProtocol
{
    /// <summary>TCP.</summary>
    Tcp = 1,

    /// <summary>UDP.</summary>
    Udp = 2,
}
