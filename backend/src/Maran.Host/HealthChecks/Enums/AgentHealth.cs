namespace Maran.Host.HealthChecks.Enums;

/// <summary>
/// What the agent is to this panel, as <see cref="AgentHealthProbe"/> measures it. On the wire it is
/// the camelCase member name (<c>"connected"</c>, <c>"unavailable"</c>, <c>"incompatible"</c>); the
/// installer and the end-to-end scripts match those words by value, so the spelling is a contract.
/// </summary>
public enum AgentHealth
{
    /// <summary>The handshake succeeded within the timeout and the revision is inside the window.</summary>
    Connected,

    /// <summary>
    /// The agent could not be reached, did not answer in time, or answered the handshake with a
    /// failure instead of its identity — an absent agent, a restarting one, a refused peer credential.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The agent answered, in a contract revision outside the window this panel drives
    /// (<see cref="AgentProtoVersionWindow"/>): an upgrade replaced one side and not the other. The only
    /// answer that fails readiness on the agent's account, because traffic sent now would be served by a
    /// panel driving an agent whose answers it may misread.
    /// </summary>
    Incompatible,
}
