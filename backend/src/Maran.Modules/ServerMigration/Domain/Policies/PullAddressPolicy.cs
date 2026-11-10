using System.Net;
using System.Net.Sockets;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// Which addresses the receiving panel will not connect to when it pulls a package (issue #127): the ones that
/// cannot be another Maran server, and the ones that would turn the pull into a way to reach this server's own
/// services.
/// </summary>
/// <remarks>
/// <para>
/// <b>Judged on the address connected to, never on the URL's text.</b> A name that resolves to 127.0.0.1 is the
/// attack, and a text check passes it. The connector applies this to every address a name resolves to, and
/// to the socket's remote address after the connection is made.
/// </para>
/// <para>
/// <b>What is refused:</b> loopback, the unspecified address, link-local (169.254.0.0/16 and fe80::/10, which holds
/// the cloud metadata address), multicast, the IPv4 broadcast address, and any address this server's own interfaces
/// hold. <b>What is allowed on purpose:</b> private ranges (10/8, 172.16/12, 192.168/16, fc00::/7), because a source
/// on the same network is a normal case. That means an administrator can make this panel send one authenticated
/// GET to an internal https service; the request carries only the grant and the answer is parsed as a package, so
/// the primitive returns nothing a non-package service would reveal beyond a refusal. The threat note states this.
/// </para>
/// </remarks>
public static class PullAddressPolicy
{
    /// <summary>Tells whether the panel must not connect to an address.</summary>
    /// <param name="address">The address, in any form.</param>
    /// <param name="own">The addresses this server's own interfaces hold.</param>
    /// <returns><c>true</c> when the address is one of the refused kinds.</returns>
    public static bool IsRefused(IPAddress address, IReadOnlySet<IPAddress> own)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(own);

        var plain = Ipv4MappedAddress.Unwrap(address);

        if (IPAddress.IsLoopback(plain)
            || plain.Equals(IPAddress.Any)
            || plain.Equals(IPAddress.IPv6Any)
            || plain.Equals(IPAddress.Broadcast)
            || own.Contains(plain))
        {
            return true;
        }

        if (plain.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return plain.IsIPv6LinkLocal || plain.IsIPv6Multicast;
        }

        var bytes = plain.GetAddressBytes();

        // 169.254.0.0/16 is link-local, and 224.0.0.0/4 is multicast.
        return (bytes[0] == 169 && bytes[1] == 254) || bytes[0] is >= 224 and <= 239;
    }
}
