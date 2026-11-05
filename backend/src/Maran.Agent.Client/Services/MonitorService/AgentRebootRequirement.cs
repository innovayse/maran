namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Whether the host needs a reboot to finish applying updates it already installed (issue #163).</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>RebootRequirement</c>. <see cref="Unmeasured"/> is the wire's
/// UNSPECIFIED, and a value this build cannot name, read once at the client boundary. A probe that
/// failed to run is NOT this state: the agent reports it as an error and the client returns a typed
/// failure, so <see cref="NotRequired"/> is only ever an answer the host actually gave.
/// </remarks>
public enum AgentRebootRequirement
{
    /// <summary>The reply stated no requirement the panel can name.</summary>
    Unmeasured = 0,

    /// <summary>The host reported that no reboot is pending.</summary>
    NotRequired = 1,

    /// <summary>The host reported that a reboot is pending.</summary>
    Required = 2,
}
