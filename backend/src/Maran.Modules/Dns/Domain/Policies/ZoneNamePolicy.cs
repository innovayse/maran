using Maran.Modules.Dns.Domain.Enums;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>What a zone's apex name may be, and the one spelling it is stored in.</summary>
public static class ZoneNamePolicy
{
    /// <summary>Spells a name the way it is stored: trimmed, lower case, without a trailing dot.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The stored spelling. Normalising decides nothing; <see cref="Check"/> does.</returns>
    public static string Normalize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.EndsWith('.'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>Tells whether a normalised name may be a zone apex.</summary>
    /// <param name="normalized">A name already passed through <see cref="Normalize"/>.</param>
    /// <returns>
    /// <see cref="ZoneNameVerdict.Accepted"/> for a host name of at least two letter-digit-hyphen labels,
    /// at most 253 characters, whose last label is not all digits. Underscores are not allowed in an apex:
    /// they belong to service labels below it.
    /// </returns>
    public static ZoneNameVerdict Check(string normalized)
    {
        if (normalized.Length > DnsLimits.MaxNameLength || !HostNameRule.IsHostName(normalized))
        {
            return ZoneNameVerdict.Invalid;
        }

        var topLabel = normalized[(normalized.LastIndexOf('.') + 1)..];
        var allDigits = topLabel.All(char.IsAsciiDigit);

        return allDigits ? ZoneNameVerdict.Invalid : ZoneNameVerdict.Accepted;
    }
}
