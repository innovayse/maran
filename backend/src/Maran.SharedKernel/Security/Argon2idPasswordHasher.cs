using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Security;

/// <summary>
/// The panel's only password hasher: Argon2id at <see cref="PasswordHashParameters"/>' cost,
/// encoded in PHC string format so the parameters travel with the hash and a future increase can
/// be detected per row (rules/security.md item 9).
/// </summary>
/// <remarks>
/// The encoding is <c>$argon2id$v=19$m=&lt;kib&gt;,t=&lt;passes&gt;,p=&lt;lanes&gt;$&lt;salt&gt;$&lt;hash&gt;</c>
/// with unpadded base64, which is what the PHC specification and every other Argon2 implementation
/// write. Storing the parameters inside the hash rather than assuming the current constants is what
/// makes <see cref="Verify"/> keep working after <see cref="PasswordHashParameters"/> is raised:
/// an old hash is still verified with the cost it was created at, and only then upgraded.
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    /// <summary>The algorithm segment of the encoded hash. A hash naming anything else is rejected.</summary>
    private const string AlgorithmName = "argon2id";

    /// <summary>The Argon2 version this hasher writes and accepts (0x13, as decimal, per the PHC encoding).</summary>
    private const int Version = 19;

    /// <summary>Number of <c>$</c>-separated segments in a well-formed encoded hash, including the leading empty one.</summary>
    private const int SegmentCount = 6;

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(PasswordHashParameters.SaltBytes);
        var hash = Derive(
            password,
            salt,
            PasswordHashParameters.MemoryKib,
            PasswordHashParameters.Iterations,
            PasswordHashParameters.Parallelism);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${AlgorithmName}$v={Version}$m={PasswordHashParameters.MemoryKib},t={PasswordHashParameters.Iterations},p={PasswordHashParameters.Parallelism}${ToUnpaddedBase64(salt)}${ToUnpaddedBase64(hash)}");
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        if (password is null || !TryParse(hash, out var memoryKib, out var iterations, out var parallelism, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, memoryKib, iterations, parallelism);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string hash)
    {
        if (!TryParse(hash, out var memoryKib, out var iterations, out var parallelism, out _, out _))
        {
            // An unreadable hash can never be verified again, so its owner will have to be given a
            // new one; saying "yes, rehash" is the answer that leads there rather than to silence.
            return true;
        }

        return memoryKib < PasswordHashParameters.MemoryKib
            || iterations < PasswordHashParameters.Iterations
            || parallelism < PasswordHashParameters.Parallelism;
    }

    /// <summary>Runs Argon2id with the given cost parameters.</summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="salt">The per-password salt.</param>
    /// <param name="memoryKib">Memory cost, in kibibytes.</param>
    /// <param name="iterations">Number of passes over memory.</param>
    /// <param name="parallelism">Degree of parallelism, in lanes.</param>
    /// <returns>The derived hash, <see cref="PasswordHashParameters.HashBytes"/> long.</returns>
    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(PasswordHashParameters.HashBytes);
    }

    /// <summary>
    /// Parses an encoded hash into its parameters, salt and digest. Returns false — never throws —
    /// for anything that is not a well-formed Argon2id hash of the expected version.
    /// </summary>
    /// <param name="encoded">The stored encoded hash.</param>
    /// <param name="memoryKib">Receives the memory cost, in kibibytes.</param>
    /// <param name="iterations">Receives the number of passes.</param>
    /// <param name="parallelism">Receives the degree of parallelism.</param>
    /// <param name="salt">Receives the salt bytes.</param>
    /// <param name="hash">Receives the digest bytes.</param>
    /// <returns>True when every segment was present and well-formed.</returns>
    private static bool TryParse(
        string? encoded,
        out int memoryKib,
        out int iterations,
        out int parallelism,
        out byte[] salt,
        out byte[] hash)
    {
        memoryKib = 0;
        iterations = 0;
        parallelism = 0;
        salt = [];
        hash = [];

        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var segments = encoded.Split('$');
        if (segments.Length != SegmentCount || segments[1] != AlgorithmName)
        {
            return false;
        }

        if (!TryReadTagged(segments[2], "v", out var version) || version != Version)
        {
            return false;
        }

        var costs = segments[3].Split(',');
        if (costs.Length != 3
            || !TryReadTagged(costs[0], "m", out memoryKib)
            || !TryReadTagged(costs[1], "t", out iterations)
            || !TryReadTagged(costs[2], "p", out parallelism))
        {
            return false;
        }

        return memoryKib > 0
            && iterations > 0
            && parallelism > 0
            && TryFromUnpaddedBase64(segments[4], out salt)
            && TryFromUnpaddedBase64(segments[5], out hash);
    }

    /// <summary>Reads a <c>&lt;name&gt;=&lt;integer&gt;</c> pair.</summary>
    /// <param name="segment">The segment to read, e.g. <c>m=65536</c>.</param>
    /// <param name="name">The expected name before the <c>=</c>.</param>
    /// <param name="value">Receives the parsed integer.</param>
    /// <returns>True when the segment had the expected name and a valid integer value.</returns>
    private static bool TryReadTagged(string segment, string name, out int value)
    {
        value = 0;
        var separator = segment.IndexOf('=', StringComparison.Ordinal);

        return separator == name.Length
            && segment.AsSpan(0, separator).SequenceEqual(name)
            && int.TryParse(segment.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Encodes bytes as base64 without the <c>=</c> padding the PHC format omits.</summary>
    /// <param name="value">The bytes to encode.</param>
    /// <returns>The unpadded base64 text.</returns>
    private static string ToUnpaddedBase64(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=');
    }

    /// <summary>Decodes unpadded base64, restoring the padding <see cref="Convert"/> requires.</summary>
    /// <param name="value">The unpadded base64 text.</param>
    /// <param name="decoded">Receives the decoded bytes.</param>
    /// <returns>True when the text was valid base64.</returns>
    private static bool TryFromUnpaddedBase64(string value, out byte[] decoded)
    {
        decoded = [];
        var padding = (4 - (value.Length % 4)) % 4;
        if (padding == 3)
        {
            return false;
        }

        var buffer = new byte[((value.Length + padding) / 4) * 3];
        if (!Convert.TryFromBase64String(value + new string('=', padding), buffer, out var written))
        {
            return false;
        }

        decoded = buffer[..written];
        return true;
    }
}
