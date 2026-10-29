using System.Security.Cryptography;

namespace Maran.SharedKernel.Security;

/// <summary>
/// The panel's one answer to "may this panel run with this key": the token-signing key and the
/// secret-encryption key are both judged here, so the two checks cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// Three questions, each asked of the DECODED bytes and never of the base64 text: a text comparison
/// would let a copy of the published value through with a leading space or a line break in it,
/// because <see cref="Convert.FromBase64String(string)"/> ignores whitespace and the key that is
/// actually used would be the published one.
/// </para>
/// <para>
/// A value that is empty or not base64 answers <c>false</c> to every question. That is not
/// approval: the format checks beside the callers already refuse such a value, and answering here
/// as well would only repeat their message under a misleading name.
/// </para>
/// <para>
/// The low-entropy floor is sized so that a key drawn from a uniform generator essentially never
/// trips it, because a false positive stops a correctly installed server whose encryption key
/// cannot be replaced without losing what it protects. For 32 random bytes the chance that every
/// byte is printable ASCII is about 1.7e-14, and the chance of at most sixteen distinct byte values
/// is about 2.2e-15; a longer key makes both smaller. The argument and the table behind these two
/// numbers are in docs/superpowers/notes/2026-10-26-published-sample-keys-threat-note.md, and a
/// change to either threshold is a change to that note.
/// </para>
/// <para>
/// What this type cannot see: a key that is random-looking and nonetheless public — copied from
/// another server or a template — passes every question. It detects structure and the values this
/// repository has published, not secrecy.
/// </para>
/// </remarks>
public static class SecretKeyPolicy
{
    /// <summary>The lowest byte value counted as printable ASCII (space).</summary>
    private const byte FirstPrintableAscii = 0x20;

    /// <summary>The highest byte value counted as printable ASCII (tilde).</summary>
    private const byte LastPrintableAscii = 0x7E;

    /// <summary>
    /// The most distinct byte values a key may hold and still be refused as structured. A genuine
    /// random 32-byte key has about 30; sixteen or fewer is a repeated or patterned value.
    /// </summary>
    private const int MostDistinctBytesOfAStructuredKey = 16;

    /// <summary>
    /// Development key values this repository has published (in <c>.env.example</c>, a CI workflow
    /// and the test hosts), as base64. Anyone who has read the repository holds them.
    /// </summary>
    private static readonly string[] PublishedDevelopmentKeys =
    [
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
    ];

    /// <summary>
    /// Whether <paramref name="base64Key"/> decodes to one of the development keys this repository
    /// has published.
    /// </summary>
    /// <param name="base64Key">The configured key, as base64.</param>
    /// <returns>True when the decoded bytes equal a published key's decoded bytes.</returns>
    public static bool IsPublishedDevelopmentKey(string? base64Key)
    {
        var key = Decode(base64Key);
        if (key is null)
        {
            return false;
        }

        foreach (var published in PublishedDevelopmentKeys)
        {
            if (CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(published)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="base64Key"/> decodes to bytes too structured to have come from a
    /// random generator: every byte printable ASCII (a passphrase, a hex string, any text), or at
    /// most <see cref="MostDistinctBytesOfAStructuredKey"/> distinct byte values (a repeated
    /// pattern).
    /// </summary>
    /// <param name="base64Key">The configured key, as base64.</param>
    /// <returns>True when the decoded key is structurally low-entropy.</returns>
    public static bool IsLowEntropy(string? base64Key)
    {
        var key = Decode(base64Key);
        if (key is null)
        {
            return false;
        }

        return IsAllPrintableAscii(key) || CountDistinctBytes(key) <= MostDistinctBytesOfAStructuredKey;
    }

    /// <summary>
    /// Whether the two keys are the same key: equal once decoded, or one occurring as a contiguous
    /// run of bytes anywhere inside the other. A signing key that contains the encryption key gives
    /// whoever holds it the encryption key just the same, so containment counts as equal. The
    /// answer is symmetric in its arguments.
    /// </summary>
    /// <remarks>
    /// The search is not constant-time. It runs once at startup over configuration the operator
    /// already holds, and no caller-supplied value ever reaches it.
    /// </remarks>
    /// <param name="firstBase64Key">One configured key, as base64.</param>
    /// <param name="secondBase64Key">The other configured key, as base64.</param>
    /// <returns>True when holding either key yields the other.</returns>
    public static bool AreSameKey(string? firstBase64Key, string? secondBase64Key)
    {
        var first = Decode(firstBase64Key);
        var second = Decode(secondBase64Key);
        if (first is null || second is null)
        {
            return false;
        }

        return first.Length <= second.Length
            ? second.AsSpan().IndexOf(first.AsSpan()) >= 0
            : first.AsSpan().IndexOf(second.AsSpan()) >= 0;
    }

    /// <summary>
    /// Decodes a configured key, answering null for an absent, empty or malformed value so that the
    /// three questions above leave that case to the format checks.
    /// </summary>
    /// <param name="base64Key">The configured key, as base64.</param>
    /// <returns>The decoded bytes, or null when there are none.</returns>
    private static byte[]? Decode(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Whether every byte of <paramref name="key"/> is a printable ASCII character.</summary>
    /// <param name="key">The decoded key.</param>
    /// <returns>True when the key could have been typed as text.</returns>
    private static bool IsAllPrintableAscii(byte[] key)
    {
        foreach (var value in key)
        {
            if (value < FirstPrintableAscii || value > LastPrintableAscii)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Counts how many different byte values <paramref name="key"/> holds.</summary>
    /// <param name="key">The decoded key.</param>
    /// <returns>The number of distinct byte values, from 1 to 256.</returns>
    private static int CountDistinctBytes(byte[] key)
    {
        var seen = new bool[256];
        var distinct = 0;
        foreach (var value in key)
        {
            if (!seen[value])
            {
                seen[value] = true;
                distinct++;
            }
        }

        return distinct;
    }
}
