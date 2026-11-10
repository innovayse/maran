using System.Net;

namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>Tells which addresses this server's own network interfaces hold (issue #127).</summary>
/// <remarks>
/// A seam so the address policy is tested with a chosen set; the real one reads the interfaces when asked, so an
/// address added after start-up is seen.
/// </remarks>
public interface ILocalAddressSource
{
    /// <summary>Reads the addresses of this server's interfaces.</summary>
    /// <returns>Every unicast address configured on any interface, IPv4-mapped forms unwrapped.</returns>
    IReadOnlySet<IPAddress> Read();
}
