namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>What the agent found out about one managed unit.</summary>
/// <remarks>
/// Three values and not a boolean. A monitor that can only say "running" or "stopped" has to invent
/// one of them whenever it does not know, and the invented answer is always the one that wakes
/// somebody: on the Debian family the enabled SSH unit is a socket, and the service it fronts stays
/// inactive from boot until the first connection, so a two-value reading reports an outage on every
/// such host at every reboot.
///
/// A panel-side mirror of the wire's <c>ServiceState</c> so callers outside this project never hold
/// a generated protobuf type. It has no "unspecified" member on purpose: the wire's unspecified is a
/// statement about the AGENT's age rather than about the service, and the client resolves it — from
/// the legacy boolean the old agent did send — before anything reaches this type.
/// </remarks>
public enum AgentServiceState
{
    /// <summary>The unit is up: the service manager reports it active, or reloading.</summary>
    Running = 1,

    /// <summary>
    /// The unit is down and nothing is waiting to bring it up. The one state worth waking somebody
    /// for, which is why nothing else is ever mapped onto it.
    /// </summary>
    Stopped = 2,

    /// <summary>
    /// The agent reached the service manager and can call the unit neither up nor down: a
    /// socket-activated service nothing has connected to yet, a unit mid-transition, or a unit not
    /// installed on this host. None of them is an outage, and none is proof of health.
    /// </summary>
    Unknown = 3,
}
