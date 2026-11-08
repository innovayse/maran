using System.Globalization;

namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>How the panel names the DKIM keys it generates.</summary>
/// <remarks>
/// A selector is one DNS label (<c>&lt;selector&gt;._domainkey.&lt;domain&gt;</c>) and a file name on the host,
/// so the agent's grammar is narrow: lower-case letters, digits and hyphens, at most 32 characters. The
/// panel's names are <c>m</c>, the date the key was made and a counter for that day, so two rotations on
/// one day never share a selector and a selector tells an operator when its key was made.
/// </remarks>
public static class DkimSelectorPolicy
{
    /// <summary>Names the selector of a key.</summary>
    /// <param name="createdAt">The instant the key is generated.</param>
    /// <param name="sequenceOnThatDay">How many keys of this domain were already made that UTC day.</param>
    /// <returns>For example <c>m20261107-1</c>.</returns>
    public static string For(DateTimeOffset createdAt, int sequenceOnThatDay)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"m{createdAt.UtcDateTime:yyyyMMdd}-{sequenceOnThatDay + 1}");
    }

    /// <summary>Whether a key made at <paramref name="createdAt"/> belongs to the same UTC day as <paramref name="other"/>.</summary>
    /// <param name="createdAt">The key's creation instant.</param>
    /// <param name="other">The instant being compared against.</param>
    /// <returns>True when both fall on one UTC calendar day.</returns>
    public static bool SameDay(DateTimeOffset createdAt, DateTimeOffset other)
    {
        return createdAt.UtcDateTime.Date == other.UtcDateTime.Date;
    }
}
