using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The law of the one record the certificate facility may write into a zone: a DNS-01 challenge, a TXT at
/// <c>_acme-challenge</c> whose value is a SHA-256 digest. It is stateless and judges text only; the zone
/// applies it.
/// </summary>
/// <remarks>
/// The shape is the whole defence of the door the certificate module writes through. A value of forty-three
/// base64url characters cannot be a SPF line, a DKIM key or a CNAME target, so even a caller that is wrong
/// about what it is publishing cannot put anything but a challenge into a customer's zone.
/// </remarks>
public static class ChallengeRecordPolicy
{
    /// <summary>The label a DNS-01 challenge lives under (RFC 8555 §8.4).</summary>
    public const string Label = "_acme-challenge";

    /// <summary>The length of a base64url SHA-256 digest without padding, which is what a challenge value is.</summary>
    public const int ValueLength = 43;

    /// <summary>
    /// The most challenge values that may sit at one name. A wildcard order proves the name and its wildcard
    /// at the same label, so two is the number a single order needs and the number that bounds the rows.
    /// </summary>
    public const int MaxValuesPerName = 2;

    /// <summary>How long a challenge record may stay before it is taken for an orphan and swept.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    /// <summary>Tells whether a value is exactly a base64url digest: forty-three letters, digits, hyphens or underscores.</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns>True when nothing but the challenge alphabet is present, at exactly the digest's length.</returns>
    public static bool IsValue(string? value)
    {
        if (value is null || value.Length != ValueLength)
        {
            return false;
        }

        return value.All(character =>
        {
            return char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';
        });
    }

    /// <summary>Tells whether a hostname is a plain lower-case host name a challenge can be placed under.</summary>
    /// <param name="hostname">The candidate name.</param>
    /// <returns>
    /// True for two or more well-formed labels in lower case. A wildcard label, a trailing dot, an upper-case
    /// letter and the challenge label itself are all refused, so one name has one spelling.
    /// </returns>
    public static bool IsHostname(string? hostname)
    {
        return hostname is not null
            && HostNameRule.IsHostName(hostname)
            && hostname.Length <= DnsLimits.MaxNameLength
            && hostname.All(character =>
            {
                return !char.IsAsciiLetterUpper(character);
            });
    }

    /// <summary>Lists the names a hostname could be served from, longest first: itself, then each parent of two or more labels.</summary>
    /// <param name="hostname">A name accepted by <see cref="IsHostname"/>.</param>
    /// <returns>The candidate zone apexes, most specific first.</returns>
    public static IReadOnlyList<string> ZoneCandidates(string hostname)
    {
        var labels = hostname.Split('.');
        var candidates = new List<string>();
        for (var start = 0; start <= labels.Length - 2; start++)
        {
            candidates.Add(string.Join('.', labels.Skip(start)));
        }

        return candidates;
    }

    /// <summary>Spells the challenge record's owner name relative to its zone.</summary>
    /// <param name="zoneName">The zone's apex, which must equal the hostname or be a parent of it.</param>
    /// <param name="hostname">The name being validated.</param>
    /// <returns><c>_acme-challenge</c> for the apex, else <c>_acme-challenge.</c> followed by the part of the hostname below the apex.</returns>
    public static string RecordNameIn(string zoneName, string hostname)
    {
        if (hostname == zoneName)
        {
            return Label;
        }

        return Label + "." + hostname[..(hostname.Length - zoneName.Length - 1)];
    }

    /// <summary>Tells whether a stored owner name is a challenge name: the label itself or something below it.</summary>
    /// <param name="recordName">An owner name relative to its zone, in stored spelling.</param>
    /// <returns>True for <c>_acme-challenge</c> and for <c>_acme-challenge.</c> followed by anything.</returns>
    public static bool IsChallengeName(string recordName)
    {
        return recordName == Label || recordName.StartsWith(Label + ".", StringComparison.Ordinal);
    }
}
