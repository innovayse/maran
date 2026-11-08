using System.Net;
using System.Net.Sockets;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Host.Demo.Pseudonymisation;

/// <summary>
/// The network a visitor is counted as: a whole IPv4 address, or the /64 an IPv6 address belongs to.
/// </summary>
/// <remarks>
/// An ordinary IPv6 connection owns every address of its /64 (2^64 of them). Keying the stand-in, the
/// mutation budget and the rate limits on the full address would give one visitor an unlimited number of
/// distinct visitors, so the budgets would bound nobody (threat note T6). The /64 is the smallest unit
/// an operator hands to one subscriber, so it is the unit a visitor is counted as.
/// </remarks>
public static class VisitorNetwork
{
    /// <summary>Bytes of an IPv6 address that name the /64: the first eight.</summary>
    private const int PrefixBytes = 8;

    /// <summary>The address the visitor is counted as.</summary>
    /// <param name="reported">The address the connection reported.</param>
    /// <returns>
    /// The plain IPv4 address, or the IPv6 address with its lower 64 bits cleared; an IPv4-mapped IPv6
    /// address is unwrapped first and a scope id removed, as everywhere else in the panel.
    /// </returns>
    public static IPAddress Of(IPAddress reported)
    {
        ArgumentNullException.ThrowIfNull(reported);

        var plain = ScopelessAddress.Strip(Ipv4MappedAddress.Unwrap(reported));
        if (plain.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return plain;
        }

        var bytes = plain.GetAddressBytes();
        Array.Clear(bytes, PrefixBytes, bytes.Length - PrefixBytes);
        return new IPAddress(bytes);
    }
}
