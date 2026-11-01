namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>What the agent found out about the host clock's synchronisation (issue #150).</summary>
/// <remarks>
/// The kernel's own verdict, not a measured offset: the kernel stops reporting its clock
/// synchronised once its estimate of the possible error passes sixteen seconds, which is how a clock
/// left drifting shows up here. A panel-side mirror of the wire's <c>ClockSyncState</c>, so callers
/// outside this project never hold a generated protobuf type. It has no "unspecified" member: a
/// wire value this build cannot name is a reply the client refuses, never a state.
/// </remarks>
public enum AgentClockSyncState
{
    /// <summary>The kernel reports its clock synchronised.</summary>
    Synchronized = 1,

    /// <summary>
    /// A synchronisation service is enabled, but the kernel does not report its clock synchronised:
    /// no time source reached yet, or not for long enough that the clock may have drifted.
    /// </summary>
    NotSynchronized = 2,

    /// <summary>No synchronisation service is enabled and the clock is not synchronised.</summary>
    SyncServiceDisabled = 3,
}
