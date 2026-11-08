using System.Text;

namespace Maran.Agent.Simulated.Usage;

/// <summary>
/// A hash of a string that is the same on every run, every machine and every process, so a figure
/// derived from an account name is the same figure each time the demonstration is looked at.
/// </summary>
/// <remarks>
/// <c>string.GetHashCode</c> is randomised per process and cannot be used for this. The function is
/// FNV-1a over the UTF-8 bytes: not a security primitive, and not used as one.
/// </remarks>
public static class StableHash
{
    /// <summary>The FNV-1a 64-bit offset basis.</summary>
    private const ulong OffsetBasis = 14695981039346656037UL;

    /// <summary>The FNV-1a 64-bit prime.</summary>
    private const ulong Prime = 1099511628211UL;

    /// <summary>Hashes <paramref name="value"/> with 64-bit FNV-1a over its UTF-8 bytes.</summary>
    /// <param name="value">The text to hash.</param>
    /// <returns>A value that depends on nothing but the text.</returns>
    public static ulong Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hash = OffsetBasis;
        foreach (var octet in Encoding.UTF8.GetBytes(value))
        {
            hash ^= octet;
            hash = unchecked(hash * Prime);
        }

        return hash;
    }
}
