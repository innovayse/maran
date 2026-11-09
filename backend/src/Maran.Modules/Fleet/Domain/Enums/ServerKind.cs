namespace Maran.Modules.Fleet.Domain.Enums;

/// <summary>
/// What a registered server is to this panel. The two kinds differ in how the panel reaches them, not
/// in what they are: the panel's own host is reached over its unix socket, a node over a pinned,
/// mutually authenticated TCP connection.
/// </summary>
public enum ServerKind
{
    /// <summary>
    /// The server the panel itself runs on. Exactly one exists, it is seeded, it is reached over the
    /// agent's unix socket, and it can be neither disabled nor removed.
    /// </summary>
    Local = 0,

    /// <summary>
    /// A server an administrator enrolled: an agent on another host, reached over the network once the
    /// administrator has approved the certificate it presented.
    /// </summary>
    Node = 1,
}
