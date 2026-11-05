using System.Buffers.Binary;
using System.Text;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// Real OpenSSH public keys, and builders for the malformed or boundary keys a real generator will
/// not produce.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Ed25519</c>, <c>P256</c>, <c>P384</c>, <c>P521</c> and <c>Rsa*</c> constants were generated
/// with <c>ssh-keygen -t ... -N ''</c> and their fingerprints read back with
/// <c>ssh-keygen -lf &lt;file&gt; -E sha256</c>, so an expectation here is the answer of OpenSSH itself
/// and not of the code under test. The hardware-backed <c>sk-*</c> algorithms need a security key to
/// generate, so they are built by hand from the wire layout instead.
/// </para>
/// <para>
/// The builders write the SSH wire encoding (RFC 4251 section 5): each field is a big-endian u32
/// length followed by that many bytes.
/// </para>
/// </remarks>
public static class SftpTestKeys
{
    /// <summary>An Ed25519 key, as <c>ssh-keygen -t ed25519</c> wrote it (comment removed).</summary>
    public const string Ed25519 =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEW1RqkHK7gbstpNOc9T2ewxrU/7tkKJ9mGLyAgvzkiW";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="Ed25519"/>.</summary>
    public const string Ed25519Fingerprint = "SHA256:Y9EIl/EUA+HWT86AC95l3/p7tkOiVH7ROv4k17uGIfw";

    /// <summary>An ECDSA key on NIST P-256.</summary>
    public const string P256 =
        "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBER8mYFZhvd0px6LeUmU9fgTkhQDULggahhC"
        + "wQquFs6Wo2wnIzpJL67MeUhMnJBGorBNJrqjj4wVQpa665izinQ=";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="P256"/>.</summary>
    public const string P256Fingerprint = "SHA256:h5OeKTzYB+sRzsWDVvhcl+zjhFORtg42+xWdO6Uvflo";

    /// <summary>An ECDSA key on NIST P-384.</summary>
    public const string P384 =
        "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBEw/Y15DZWJdPeidwF/fZI22cTe1gFTCeh42"
        + "sv9sY4LKTgBInZFq9i0byzLWVXJoVAQ2658ZPXyxUX6geCP7cUGB4mEEG8+x+8CHsR1UgulTp6kOOaaudz4niN3r5b4jDw==";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="P384"/>.</summary>
    public const string P384Fingerprint = "SHA256:OZY7C5wRWKaKIIQ7+UgzRHoiMoqAoh1BVPxlLUDHFME";

    /// <summary>An ECDSA key on NIST P-521.</summary>
    public const string P521 =
        "ecdsa-sha2-nistp521 AAAAE2VjZHNhLXNoYTItbmlzdHA1MjEAAAAIbmlzdHA1MjEAAACFBAELgDHts8cWSB8to+h73kvNmq7m5a3/mAOG"
        + "BluC7tmXSc7GTB7L2DWSVlCig0XeWBfeh8xC0YkB/T1rWk6Bl7xviADiI+JV2MyCzmej6Uv8PE1Hd55wScHefv9rTbMPGIwZLBIsjHkbCKnv"
        + "cXwNAguVkmP3rdfYWDYGqnQcWqwH+7MQnA==";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="P521"/>.</summary>
    public const string P521Fingerprint = "SHA256:3ZgBG0KAgH96AcNxYH+2mO5o0wLaLrDNQErEzPYuRKQ";

    /// <summary>A 1024-bit RSA key: below the agent's 2048-bit floor, so refused.</summary>
    public const string Rsa1024 =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAAAgQDllo+aYHs81dvPsgjDDwdBdE0qCGFD9D2KkKoDwOrEgvarDKeu7PTp2vUrzQ/IAmNEw6Hr"
        + "+ZxcpUgI7aZTDI8L3uoH46nwDZ96CBfteYbIH2pyxU5PbRQq+RvJC3bto3aFNVe6zz5ubOk/X78dGq0xD6+p5TQPivzA9le0jPY/gQ==";

    /// <summary>A 2048-bit RSA key: exactly the agent's floor, so accepted.</summary>
    public const string Rsa2048 =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQCF2d2FRf7JEDIiUq5FupPcv86UZ5HRztk/cskdqc9tVX+qe3QL3xZUCfOoO3ctSINmjUnd"
        + "937RBGzPZrQ+nsSuxcO6pkTUzVgXqy+TGOzuDWlZyKm1BM5XNu47Cf5L1DEgnyeJE5hxZ0b1eVEitxzAddVVlOSHew9eCCkT1koUMFf9lq/H"
        + "bzUfx5gtAiIR3yreprvhA5/4yiZLSwOLE1eW7H7FwuFTosITboVr5U9F44YmKuIoXcBavHL+KzxiJhVEss8okzIZezxzbR7cFAdlCAkzFMqO"
        + "XwOQmYSZvW8gIrBd52iBB1/t6dbPIMvUD4vfB6rp+9XFJjYWf1PoB6hx";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="Rsa2048"/>.</summary>
    public const string Rsa2048Fingerprint = "SHA256:QDK2gT/m9JugBje4cgepM2gHhpBu92uTZvB3jG+8iVc";

    /// <summary>A 3072-bit RSA key.</summary>
    public const string Rsa3072 =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQC6aFeQEJtgbQYNjN6cDN2BiShRxiG9ykt/iGlg4eheczCKHhKGZ0CKOVJg95C7VAHafXEJ"
        + "zWTjcp0n0E+gNWOaRQQBiJi5RFeRjwGhfmwOpwtlaY+KYlFvuuCerb85hUaePg/2ueNYLdnsqtdjgLpytNp1SxTnL4ebrISsAmTRjm9lLLxr"
        + "f7Fe8chgzGpbZKsD/Wb6hY193wEV2vClZMawRGCQlvtxR97QyY9XNTCRIz6xaWeLAsICMPByLkzo2eJzwm/BDdcg+NEqcYQz7eX42GpvtYAGg"
        + "eGY2S6RuRnIRN770e/Ae9Ev3Cu17Cv37MNW5WqH6vrs5auhxDNaodQXXKWOHrHCnriJdsDjdry9z9RHUsNEoXKoAdABr2IOY/hePtghKWL21"
        + "Q7oA0r7JVJ4xE47jiHUquS6NvzKj+trBlFMALnJVoyz2YtZIJ71ExjamdD+e914fw/Ca6NZsue69MJYKMU5869gn61g00EsHv1q69m0Kpqr6"
        + "x5GxtYtBAE=";

    /// <summary>The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for <see cref="Rsa3072"/>.</summary>
    public const string Rsa3072Fingerprint = "SHA256:jAyvwDNVT5bt7qnTzrdJpBicyeuJMenTQOjVzHO8wWg";

    /// <summary>Builds a well-formed Ed25519 key whose 32 key bytes are all <paramref name="fill"/>.</summary>
    /// <param name="fill">The byte repeated through the key; different fills give different fingerprints.</param>
    /// <returns>The key as <c>ssh-ed25519 &lt;base64&gt;</c>.</returns>
    public static string Ed25519Filled(byte fill)
    {
        var blob = Concatenate(WireString("ssh-ed25519"), WireBytes(Enumerable.Repeat(fill, 32).ToArray()));

        return "ssh-ed25519 " + Convert.ToBase64String(blob);
    }

    /// <summary>Builds the <paramref name="index"/>-th key of a run of well-formed, pairwise distinct keys.</summary>
    /// <param name="index">Zero-based position; 0 to 1023 give distinct keys.</param>
    /// <returns>An Ed25519 key unlike every other index.</returns>
    public static string Distinct(int index)
    {
        var material = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(material.AsSpan(0, sizeof(int)), index);
        material[31] = 0x5A;

        var blob = Concatenate(WireString("ssh-ed25519"), WireBytes(material));

        return "ssh-ed25519 " + Convert.ToBase64String(blob);
    }

    /// <summary>Builds a key of <paramref name="algorithm"/> from raw blob bytes, bypassing every generator.</summary>
    /// <param name="algorithm">The algorithm name written in the first field.</param>
    /// <param name="blob">The bytes encoded as the second field.</param>
    /// <returns>The key line.</returns>
    public static string FromBlob(string algorithm, byte[] blob)
    {
        return algorithm + " " + Convert.ToBase64String(blob);
    }

    /// <summary>Decodes the blob of a key line.</summary>
    /// <param name="line">A key as <c>&lt;algorithm&gt; &lt;base64&gt;</c>, without a comment.</param>
    /// <returns>The decoded second field.</returns>
    public static byte[] BlobOf(string line)
    {
        return Convert.FromBase64String(line.Split(' ')[1]);
    }

    /// <summary>Builds an RSA key whose modulus has exactly <paramref name="bits"/> bits.</summary>
    /// <param name="bits">The modulus size; the top bit is set and the rest is a fixed pattern.</param>
    /// <returns>An <c>ssh-rsa</c> line that is well formed on the wire.</returns>
    public static string RsaOfBits(int bits)
    {
        // The most significant byte holds (bits % 8) bits, or a full byte when bits is a multiple of 8.
        var length = (bits + 7) / 8;
        var modulus = new byte[length];
        Array.Fill(modulus, (byte)0xAB);
        var topBits = bits - ((length - 1) * 8);
        modulus[0] = (byte)(1 << (topBits - 1));

        var blob = Concatenate(WireString("ssh-rsa"), WireBytes([0x01, 0x00, 0x01]), WireBytes(modulus));

        return "ssh-rsa " + Convert.ToBase64String(blob);
    }

    /// <summary>Builds a hardware-backed Ed25519 key (<c>sk-ssh-ed25519@openssh.com</c>).</summary>
    /// <returns>A key with the 32-byte public key and the application string the layout requires.</returns>
    public static string SkEd25519()
    {
        var blob = Concatenate(
            WireString("sk-ssh-ed25519@openssh.com"),
            WireBytes(Enumerable.Repeat((byte)0x11, 32).ToArray()),
            WireString("ssh:"));

        return "sk-ssh-ed25519@openssh.com " + Convert.ToBase64String(blob);
    }

    /// <summary>Builds a hardware-backed P-256 key (<c>sk-ecdsa-sha2-nistp256@openssh.com</c>).</summary>
    /// <returns>A key with an uncompressed P-256 point and the application string the layout requires.</returns>
    public static string SkEcdsaP256()
    {
        var point = new byte[65];
        point[0] = 0x04;
        Array.Fill(point, (byte)0x22, 1, 64);

        var blob = Concatenate(
            WireString("sk-ecdsa-sha2-nistp256@openssh.com"),
            WireString("nistp256"),
            WireBytes(point),
            WireString("ssh:"));

        return "sk-ecdsa-sha2-nistp256@openssh.com " + Convert.ToBase64String(blob);
    }

    /// <summary>Encodes a text as one SSH wire string.</summary>
    /// <param name="text">ASCII text.</param>
    /// <returns>A u32 length followed by the bytes.</returns>
    public static byte[] WireString(string text)
    {
        return WireBytes(Encoding.ASCII.GetBytes(text));
    }

    /// <summary>Encodes bytes as one SSH wire string.</summary>
    /// <param name="value">The field's bytes.</param>
    /// <returns>A big-endian u32 length followed by the bytes.</returns>
    public static byte[] WireBytes(byte[] value)
    {
        var encoded = new byte[sizeof(uint) + value.Length];
        BinaryPrimitives.WriteUInt32BigEndian(encoded, (uint)value.Length);
        value.CopyTo(encoded, sizeof(uint));

        return encoded;
    }

    /// <summary>Joins byte arrays in order.</summary>
    /// <param name="parts">The pieces.</param>
    /// <returns>One array holding all of them.</returns>
    public static byte[] Concatenate(params byte[][] parts)
    {
        return parts.SelectMany(part =>
        {
            return part;
        }).ToArray();
    }
}
