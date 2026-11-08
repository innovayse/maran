using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Maran.Host.Demo.Pseudonymisation;

/// <summary>
/// Turns a visitor's real address into a stable, unlinkable stand-in under a key that exists only in
/// this process's memory (threat note T9).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it keeps.</b> The same address gives the same stand-in for as long as the key lives, so rate
/// limits, the mutation budget and the session list still tell visitors apart. <b>What it drops.</b> The
/// key is generated at construction and written nowhere; a reset restarts the process, the key changes,
/// and the stand-ins of one interval cannot be matched to the next, or to any real address.
/// </para>
/// <para>
/// <b>One stand-in per network, not per address.</b> An IPv6 address is first reduced to its /64
/// (<see cref="VisitorNetwork"/>), so a visitor who owns a whole /64 is one visitor and not 2^64.
/// </para>
/// <para>
/// <b>Where the stand-in lives.</b> The first twelve bytes of an HMAC-SHA256 of the visitor network
/// (<see cref="VisitorNetwork.Of"/>), placed under <c>2001:db8::/32</c>, the IPv6 documentation prefix
/// (RFC 3849), which is never routed: no stand-in can be mistaken for, or banned as, a real host.
/// </para>
/// </remarks>
public sealed class ClientPseudonymiser
{
    /// <summary>Bytes of the HMAC placed after the prefix: 16 minus the prefix.</summary>
    private const int HashBytes = 12;

    /// <summary>The documentation prefix <c>2001:db8::/32</c> every stand-in starts with.</summary>
    private static readonly byte[] Prefix = [0x20, 0x01, 0x0d, 0xb8];

    /// <summary>The HMAC key.</summary>
    private readonly byte[] _key;

    /// <summary>Creates a pseudonymiser with a fresh random key.</summary>
    public ClientPseudonymiser()
        : this(RandomNumberGenerator.GetBytes(32))
    {
    }

    /// <summary>Creates a pseudonymiser over a given key.</summary>
    /// <param name="key">The HMAC key; tests use this to prove a key change changes the stand-ins.</param>
    public ClientPseudonymiser(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
    }

    /// <summary>The stand-in for an address.</summary>
    /// <param name="real">The address the connection reported.</param>
    /// <returns>
    /// An IPv6 address under the documentation prefix; two addresses of one IPv6 /64 give the same one.
    /// </returns>
    public IPAddress Of(IPAddress real)
    {
        ArgumentNullException.ThrowIfNull(real);

        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(VisitorNetwork.Of(real).ToString()));
        var bytes = new byte[16];
        Prefix.CopyTo(bytes, 0);
        Array.Copy(hash, 0, bytes, Prefix.Length, HashBytes);
        return new IPAddress(bytes);
    }
}
