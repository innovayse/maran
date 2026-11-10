namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// The shape of the fingerprint ssh prints for a host key: <c>SHA256:</c> followed by the 43 unpadded base64
/// characters of a SHA-256 digest. Whether a well-formed fingerprint is the right one is a different question, and
/// the registry answers it by comparing with what the server offered.
/// </summary>
public static class HostKeyFingerprintPolicy
{
    /// <summary>The prefix that names the digest.</summary>
    public const string Prefix = "SHA256:";

    /// <summary>The length of an unpadded base64 SHA-256 digest.</summary>
    public const int DigestLength = 43;

    /// <summary>Whether a text has the shape of a host key fingerprint.</summary>
    /// <param name="fingerprint">The text as the administrator typed it; compared exactly, not trimmed or lowered.</param>
    /// <returns>True for <c>SHA256:</c> and exactly 43 base64 characters.</returns>
    public static bool IsWellFormed(string? fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != Prefix.Length + DigestLength)
        {
            return false;
        }

        if (!fingerprint.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return fingerprint.Skip(Prefix.Length).All(character =>
        {
            return char.IsAsciiLetterOrDigit(character) || character == '+' || character == '/';
        });
    }
}
