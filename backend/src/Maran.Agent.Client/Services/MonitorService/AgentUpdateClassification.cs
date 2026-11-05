namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Whether the host's package manager marks a pending update as a security update (issue #163).</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>UpdateClassification</c>, so callers outside this project never
/// hold a generated protobuf type. <see cref="Unmeasured"/> is the wire's UNSPECIFIED read once at the
/// client boundary: an agent older than contract revision 19 never sets the field, and the agent
/// itself leaves it unset when the family could not tell. It is a state of its own and is never
/// folded into <see cref="Ordinary"/>, because "ordinary" is a measured answer and an update nobody
/// classified must not be reported as one that is not urgent.
/// </remarks>
public enum AgentUpdateClassification
{
    /// <summary>The agent did not measure it: an older agent, or a family that could not tell.</summary>
    Unmeasured = 0,

    /// <summary>The package manager's metadata marks the update as a security update.</summary>
    Security = 1,

    /// <summary>The update was classified, and it is not a security update.</summary>
    Ordinary = 2,
}
