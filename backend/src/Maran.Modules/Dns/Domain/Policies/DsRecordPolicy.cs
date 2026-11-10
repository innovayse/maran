using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Computes the DS record that goes with a zone's key (RFC 4034 section 5.1.4, digest type 2 from RFC 4509):
/// the SHA-256 of the zone's canonical owner name in wire form followed by the DNSKEY RDATA. The digest is
/// a pure function of those two inputs; the panel states it so the owner does not have to run a tool, and the
/// agent-side polygon suite compares it with a host's own <c>dnssec-dsfromkey</c>.
/// </summary>
public static class DsRecordPolicy
{
    /// <summary>The DS digest type for SHA-256.</summary>
    public const int DigestType = 2;

    /// <summary>The longest label a name may hold, in bytes.</summary>
    private const int MaxLabelLength = 63;

    /// <summary>The DS record for a zone's key.</summary>
    /// <param name="zoneName">The zone's apex, as stored.</param>
    /// <param name="key">The zone's key.</param>
    /// <returns>The record the owner publishes at the registrar.</returns>
    public static DsRecord For(string zoneName, ZoneSigningKey key)
    {
        return Compute(zoneName, key.Flags, key.Algorithm, Convert.FromBase64String(key.PublicKey));
    }

    /// <summary>The DS record for any DNSKEY, whatever its algorithm.</summary>
    /// <param name="zoneName">The owner name of the DNSKEY, with or without the trailing dot, in any case.</param>
    /// <param name="flags">The DNSKEY flags.</param>
    /// <param name="algorithm">The DNSKEY algorithm number.</param>
    /// <param name="publicKey">The DNSKEY public key bytes.</param>
    /// <returns>The record, with its digest in upper-case hexadecimal.</returns>
    /// <exception cref="ArgumentException">The name is not a plain ASCII name made of labels of 1 to 63 bytes.</exception>
    public static DsRecord Compute(string zoneName, int flags, int algorithm, byte[] publicKey)
    {
        var owner = WireName(zoneName);
        var rdata = DnssecKeyPolicy.DnskeyRdata(flags, algorithm, publicKey);
        var digest = SHA256.HashData(owner.Concat(rdata).ToArray());

        return new DsRecord(
            DnssecKeyPolicy.KeyTag(flags, algorithm, publicKey),
            algorithm,
            DigestType,
            Convert.ToHexString(digest));
    }

    /// <summary>Spells a name in canonical wire form (RFC 4034 section 6.2): lower case, each label length-prefixed, a zero byte at the end.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The wire bytes.</returns>
    private static byte[] WireName(string name)
    {
        var wire = new List<byte>();
        foreach (var label in name.TrimEnd('.').ToLowerInvariant().Split('.'))
        {
            if (label.Length is 0 or > MaxLabelLength || HasNonAscii(label))
            {
                throw new ArgumentException("A zone name is made of plain ASCII labels of 1 to 63 bytes.", nameof(name));
            }

            wire.Add((byte)label.Length);
            wire.AddRange(Encoding.ASCII.GetBytes(label));
        }

        wire.Add(0);

        return [.. wire];
    }

    /// <summary>Tells whether a label holds a character outside plain ASCII.</summary>
    /// <param name="label">The label.</param>
    /// <returns><see langword="true"/> when any character is above 0x7F.</returns>
    private static bool HasNonAscii(string label)
    {
        foreach (var character in label)
        {
            if (character > 0x7F)
            {
                return true;
            }
        }

        return false;
    }
}
