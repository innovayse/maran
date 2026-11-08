namespace Maran.SharedKernel.Constants;

/// <summary>
/// The well-known identity of the server the panel itself runs on. Every panel has exactly one such
/// server, it always exists, and it is the target of every agent call that does not name another, so
/// a panel with no managed nodes behaves as it did before nodes existed.
/// </summary>
public static class FleetServers
{
    /// <summary>The id of the server this panel runs on; the default agent target.</summary>
    public static readonly Guid LocalId = new("00000000-0000-0000-0000-000000000001");
}
