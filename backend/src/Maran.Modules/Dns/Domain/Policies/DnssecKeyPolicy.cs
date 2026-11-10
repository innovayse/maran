using System.Security.Cryptography;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The one kind of signing key the panel makes and the arithmetic around it: ECDSA over P-256 (DNSSEC
/// algorithm 13), kept as one combined signing key (flags 257), with its public half as the 64 bytes
/// <c>x || y</c> a DNSKEY record carries and its private half as the 32-byte scalar. The key comes from the
/// platform's own <see cref="ECDsa"/>; nothing here is hand-written cryptography beyond the key-tag sum
/// RFC 4034 appendix B defines.
/// </summary>
public static class DnssecKeyPolicy
{
    /// <summary>The DNSSEC algorithm number of ECDSA P-256 with SHA-256 (RFC 6605).</summary>
    public const int Algorithm = 13;

    /// <summary>The DNSKEY flags of a zone-signing key that is also the secure entry point: the combined signing key.</summary>
    public const int Flags = 257;

    /// <summary>The DNSKEY protocol field, which is always 3.</summary>
    public const int Protocol = 3;

    /// <summary>The length of the public key in bytes: the x and the y coordinate, 32 bytes each.</summary>
    public const int PublicKeyLength = 64;

    /// <summary>The length of the private scalar in bytes.</summary>
    public const int PrivateKeyLength = 32;

    /// <summary>The length of one coordinate or the scalar, in bytes.</summary>
    private const int ScalarLength = 32;

    /// <summary>Makes a new key.</summary>
    /// <returns>The key, with its key tag.</returns>
    public static DnssecKeyMaterial Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);

        return FromParts(parameters.Q.X!, parameters.Q.Y!, parameters.D!);
    }

    /// <summary>Assembles a key from its three numbers; the way a key made elsewhere is described.</summary>
    /// <param name="x">The public x coordinate, at most 32 bytes, big-endian.</param>
    /// <param name="y">The public y coordinate, at most 32 bytes, big-endian.</param>
    /// <param name="d">The private scalar, at most 32 bytes, big-endian.</param>
    /// <returns>The key, with each number padded on the left to 32 bytes and its key tag computed.</returns>
    /// <exception cref="ArgumentException">A number is longer than 32 bytes.</exception>
    public static DnssecKeyMaterial FromParts(byte[] x, byte[] y, byte[] d)
    {
        var publicKey = new byte[PublicKeyLength];
        Pad(x).CopyTo(publicKey, 0);
        Pad(y).CopyTo(publicKey, ScalarLength);

        return new DnssecKeyMaterial(
            Convert.ToBase64String(publicKey),
            new SensitiveString(Convert.ToBase64String(Pad(d))),
            Flags,
            Algorithm,
            KeyTag(Flags, Algorithm, publicKey));
    }

    /// <summary>Builds the RDATA of a DNSKEY record (RFC 4034 section 2.1): flags, protocol, algorithm, public key.</summary>
    /// <param name="flags">The DNSKEY flags.</param>
    /// <param name="algorithm">The algorithm number.</param>
    /// <param name="publicKey">The public key bytes.</param>
    /// <returns>The RDATA bytes.</returns>
    public static byte[] DnskeyRdata(int flags, int algorithm, ReadOnlySpan<byte> publicKey)
    {
        var rdata = new byte[4 + publicKey.Length];
        rdata[0] = (byte)(flags >> 8);
        rdata[1] = (byte)(flags & 0xFF);
        rdata[2] = Protocol;
        rdata[3] = (byte)algorithm;
        publicKey.CopyTo(rdata.AsSpan(4));

        return rdata;
    }

    /// <summary>Computes the key tag of a DNSKEY exactly as RFC 4034 appendix B does for every algorithm but the obsolete 1.</summary>
    /// <param name="flags">The DNSKEY flags.</param>
    /// <param name="algorithm">The algorithm number.</param>
    /// <param name="publicKey">The public key bytes.</param>
    /// <returns>The 16-bit key tag.</returns>
    public static int KeyTag(int flags, int algorithm, ReadOnlySpan<byte> publicKey)
    {
        var rdata = DnskeyRdata(flags, algorithm, publicKey);
        uint accumulator = 0;
        for (var index = 0; index < rdata.Length; index++)
        {
            accumulator += (index & 1) == 0 ? (uint)rdata[index] << 8 : rdata[index];
        }

        accumulator += (accumulator >> 16) & 0xFFFF;

        return (int)(accumulator & 0xFFFF);
    }

    /// <summary>Left-pads a big-endian number to the 32 bytes a P-256 number occupies.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The padded number.</returns>
    private static byte[] Pad(byte[] value)
    {
        if (value.Length > ScalarLength)
        {
            throw new ArgumentException("A P-256 number is at most 32 bytes.", nameof(value));
        }

        var padded = new byte[ScalarLength];
        value.CopyTo(padded, ScalarLength - value.Length);

        return padded;
    }
}
