using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Sftp.Resources;

namespace Maran.Modules.Sftp.Domain.ValueObjects;

/// <summary>
/// An OpenSSH public key accepted only in the shape an <c>authorized_keys</c> line may safely be
/// built from, with the fingerprint the panel computes from it.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONE grammar the panel has for a key, and it mirrors the agent's
/// <c>agent-core/src/validation/system/ssh_public_key.rs</c> exactly: the same allow-listed
/// algorithms, the same RSA bounds (2048 to 16384 bits), the same 8 KiB ceiling, the same refusal of
/// every control character, of a leading option, of base64 that is not canonical, of a blob naming
/// another algorithm and of trailing bytes behind a well-formed key. A panel stricter than the agent
/// is a silent second policy and a panel more lenient is refused by the agent, so a change to either
/// side moves both and the tests hold them together with the same fixtures.
/// </para>
/// <para>
/// A value of this type is a key and not merely key-shaped text. What it keeps is
/// <c>&lt;algorithm&gt; &lt;base64&gt;</c>; the comment is dropped, and an option can never be
/// present because a first field that is not an algorithm is refused.
/// </para>
/// <para>
/// The fingerprint is <c>SHA256:</c> followed by the unpadded base64 of the SHA-256 of the decoded
/// blob (the form <c>ssh-keygen -l</c> prints). It is an identifier, not a security control.
/// </para>
/// </remarks>
public sealed class SftpPublicKey
{
    /// <summary>The longest key text accepted, in bytes; the agent's <c>SshPublicKey::MAX_LENGTH</c>.</summary>
    public const int MaxLength = 8 * 1024;

    /// <summary>The longest fingerprint this type produces: <c>SHA256:</c> plus 43 base64 characters.</summary>
    public const int MaxFingerprintLength = 50;

    /// <summary>The longest algorithm name in <see cref="AcceptedAlgorithms"/>.</summary>
    public const int MaxAlgorithmLength = 34;

    /// <summary>The prefix of every fingerprint.</summary>
    private const string FingerprintPrefix = "SHA256:";

    /// <summary>The smallest RSA modulus accepted, in bits.</summary>
    private const int RsaMinBits = 2048;

    /// <summary>The largest RSA modulus accepted, in bits; OpenSSH's own ceiling.</summary>
    private const int RsaMaxBits = 16384;

    /// <summary>The length of an Ed25519 public key, in bytes.</summary>
    private const int Ed25519KeyLength = 32;

    /// <summary>The tag byte of an uncompressed elliptic-curve point.</summary>
    private const byte UncompressedPoint = 0x04;

    /// <summary>The accepted algorithm names, and nothing else; <c>ssh-dss</c> is absent on purpose.</summary>
    private static readonly string[] AcceptedAlgorithms =
    [
        "ssh-ed25519",
        "sk-ssh-ed25519@openssh.com",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "sk-ecdsa-sha2-nistp256@openssh.com",
        "ssh-rsa",
    ];

    /// <summary>The allow-listed algorithm name, as written in the first field.</summary>
    public string Algorithm { get; }

    /// <summary>The key as <c>&lt;algorithm&gt; &lt;base64&gt;</c>: no option and no comment.</summary>
    public string Line { get; }

    /// <summary><c>SHA256:</c> plus the unpadded base64 of the SHA-256 of the decoded key blob.</summary>
    public string Fingerprint { get; }

    /// <summary>Creates the value from parts the parser has already validated.</summary>
    /// <param name="algorithm">The allow-listed algorithm name.</param>
    /// <param name="line">The one-line form without option or comment.</param>
    /// <param name="fingerprint">The fingerprint of the decoded blob.</param>
    private SftpPublicKey(string algorithm, string line, string fingerprint)
    {
        Algorithm = algorithm;
        Line = line;
        Fingerprint = fingerprint;
    }

    /// <summary>Parses <paramref name="text"/> as <c>&lt;algorithm&gt; &lt;base64&gt; [comment]</c>.</summary>
    /// <param name="text">The pasted key; surrounding spaces are ignored and the comment is dropped.</param>
    /// <returns>
    /// The key, or <c>SftpKeyInvalid</c> (kind <see cref="ErrorType.Validation"/>) for anything the
    /// agent would refuse: empty or oversize text, a control character, a first field that is not an
    /// accepted algorithm (a leading option included), a second field that is absent or not canonical
    /// base64, a blob naming another algorithm or not well formed, trailing bytes, or an RSA modulus
    /// outside 2048 to 16384 bits. The failure never carries the text.
    /// </returns>
    public static Result<SftpPublicKey> Parse(string? text)
    {
        if (text is null || Encoding.UTF8.GetByteCount(text) > MaxLength)
        {
            return Invalid();
        }

        // Before any split: a newline must never get the chance to be read as a field boundary.
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                return Invalid();
            }
        }

        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || Array.IndexOf(AcceptedAlgorithms, fields[0]) < 0)
        {
            return Invalid();
        }

        var algorithm = fields[0];
        var data = fields[1];
        var blob = DecodeCanonical(data);
        if (blob is null || !IsWellFormed(algorithm, blob))
        {
            return Invalid();
        }

        var fingerprint = FingerprintPrefix + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');

        return Result<SftpPublicKey>.Ok(new SftpPublicKey(algorithm, algorithm + " " + data, fingerprint));
    }

    /// <summary>Builds the one failure this type reports.</summary>
    /// <returns>A failed result carrying <c>SftpKeyInvalid</c>.</returns>
    private static Result<SftpPublicKey> Invalid()
    {
        return Result<SftpPublicKey>.Fail(Error.Of(nameof(ErrorMessages.SftpKeyInvalid), ErrorType.Validation));
    }

    /// <summary>Decodes standard base64 and accepts only the canonical spelling of its bytes.</summary>
    /// <param name="data">The second field of the key.</param>
    /// <returns>The decoded bytes, or null when the text is not canonical padded base64.</returns>
    private static byte[]? DecodeCanonical(string data)
    {
        // Re-encoding and comparing refuses what a lenient decoder would repair: missing padding,
        // non-zero trailing bits, and any character outside the alphabet.
        try
        {
            var blob = Convert.FromBase64String(data);

            return string.Equals(Convert.ToBase64String(blob), data, StringComparison.Ordinal) ? blob : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Checks that <paramref name="blob"/> is a well-formed key of <paramref name="algorithm"/>.</summary>
    /// <param name="algorithm">The declared, allow-listed algorithm.</param>
    /// <param name="blob">The decoded key blob.</param>
    /// <returns>True when the blob names the same algorithm, is well formed, and has nothing after it.</returns>
    private static bool IsWellFormed(string algorithm, ReadOnlySpan<byte> blob)
    {
        var rest = blob;
        if (!TryReadString(ref rest, out var named) || !named.SequenceEqual(Encoding.ASCII.GetBytes(algorithm)))
        {
            return false;
        }

        var wellFormed = algorithm switch
        {
            "ssh-ed25519" => ReadExactLength(ref rest, Ed25519KeyLength),
            "sk-ssh-ed25519@openssh.com" =>
                ReadExactLength(ref rest, Ed25519KeyLength) && TryReadString(ref rest, out _),
            "ecdsa-sha2-nistp256" => ReadCurvePoint(ref rest, "nistp256", 65),
            "ecdsa-sha2-nistp384" => ReadCurvePoint(ref rest, "nistp384", 97),
            "ecdsa-sha2-nistp521" => ReadCurvePoint(ref rest, "nistp521", 133),
            "sk-ecdsa-sha2-nistp256@openssh.com" =>
                ReadCurvePoint(ref rest, "nistp256", 65) && TryReadString(ref rest, out _),
            _ => ReadRsaModulus(ref rest),
        };

        // Trailing bytes are a second structure smuggled behind a valid one.
        return wellFormed && rest.IsEmpty;
    }

    /// <summary>Reads one string and requires it to be exactly <paramref name="length"/> bytes.</summary>
    /// <param name="rest">The unread bytes; advanced past the string.</param>
    /// <param name="length">The required length.</param>
    /// <returns>True when a string of exactly that length was read.</returns>
    private static bool ReadExactLength(ref ReadOnlySpan<byte> rest, int length)
    {
        return TryReadString(ref rest, out var field) && field.Length == length;
    }

    /// <summary>Reads an ECDSA curve name and point, requiring the named curve and an uncompressed point.</summary>
    /// <param name="rest">The unread bytes; advanced past both strings.</param>
    /// <param name="curve">The curve name the algorithm requires.</param>
    /// <param name="pointLength">The required point length in bytes.</param>
    /// <returns>True when the curve is the named one and the point has the right length and form.</returns>
    private static bool ReadCurvePoint(ref ReadOnlySpan<byte> rest, string curve, int pointLength)
    {
        if (!TryReadString(ref rest, out var curveName) || !curveName.SequenceEqual(Encoding.ASCII.GetBytes(curve)))
        {
            return false;
        }

        return TryReadString(ref rest, out var point) && point.Length == pointLength && point[0] == UncompressedPoint;
    }

    /// <summary>Reads an RSA exponent and modulus and bounds the modulus size.</summary>
    /// <param name="rest">The unread bytes; advanced past both strings.</param>
    /// <returns>True when the exponent is non-empty and the modulus is 2048 to 16384 bits.</returns>
    private static bool ReadRsaModulus(ref ReadOnlySpan<byte> rest)
    {
        if (!TryReadString(ref rest, out var exponent) || Significant(exponent).IsEmpty)
        {
            return false;
        }

        if (!TryReadString(ref rest, out var modulusField))
        {
            return false;
        }

        var modulus = Significant(modulusField);
        if (modulus.IsEmpty)
        {
            return false;
        }

        var bits = ((modulus.Length - 1) * 8) + (8 - (BitOperations.LeadingZeroCount((uint)modulus[0]) - 24));

        return bits >= RsaMinBits && bits <= RsaMaxBits;
    }

    /// <summary>Strips the leading zero bytes of an <c>mpint</c>.</summary>
    /// <param name="mpint">The multi-precision integer as written on the wire.</param>
    /// <returns>The same integer without leading zero bytes.</returns>
    private static ReadOnlySpan<byte> Significant(ReadOnlySpan<byte> mpint)
    {
        var start = 0;
        while (start < mpint.Length && mpint[start] == 0)
        {
            start++;
        }

        return mpint[start..];
    }

    /// <summary>Reads one SSH wire string (RFC 4251 section 5): a big-endian u32 length and that many bytes.</summary>
    /// <param name="rest">The unread bytes; advanced past the string on success.</param>
    /// <param name="field">The string's bytes.</param>
    /// <returns>False when the length or the bytes it promises are not there.</returns>
    private static bool TryReadString(ref ReadOnlySpan<byte> rest, out ReadOnlySpan<byte> field)
    {
        field = default;
        if (rest.Length < sizeof(uint))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(rest);
        if (rest.Length - sizeof(uint) < length)
        {
            return false;
        }

        field = rest.Slice(sizeof(uint), (int)length);
        rest = rest[(sizeof(uint) + (int)length)..];

        return true;
    }
}
