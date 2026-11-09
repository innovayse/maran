namespace Maran.Modules.Fleet.Domain.Enums;

/// <summary>
/// Where a server stands in its life with this panel. The panel dials a server only in the states for
/// which <see cref="Entities.Server.MayBeDialled"/> is true, so the order of the transitions below is
/// the security property: nothing is dialled before an administrator approved it.
/// </summary>
public enum ServerState
{
    /// <summary>
    /// Declared by an administrator, or enrolled by a host but not yet approved. The panel does NOT
    /// dial a pending server: a host that presents a certificate has proved nothing until the
    /// administrator has compared its fingerprint with the one the host printed on its own terminal.
    /// </summary>
    Pending = 0,

    /// <summary>The last health observation reached the agent and its contract revision is supported.</summary>
    Online = 1,

    /// <summary>
    /// Approved, but the panel has no successful health observation yet or the last one failed. This is
    /// the state approval and re-enabling leave a node in, because an approval is not an observation.
    /// </summary>
    Unreachable = 2,

    /// <summary>The agent answered, but its contract revision is outside what this panel supports.</summary>
    Incompatible = 3,

    /// <summary>An administrator switched the server off. It keeps its pinned fingerprint and is not dialled.</summary>
    Disabled = 4,
}
