using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// What the panel accepts as the address of a remote server: a DNS name or a bare IP address literal. Whether an
/// address is somewhere the panel's own host must never connect to (loopback, link-local, this host itself) is
/// not decided here — the agent judges the address a name resolves to, which is the only moment it is known.
/// </summary>
public static class SourceHostPolicy
{
    /// <summary>The spelling stored and compared: surrounding space removed and lower case.</summary>
    /// <param name="host">The host as the administrator typed it.</param>
    /// <returns>The normalised host.</returns>
    public static string Normalize(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.Trim().ToLowerInvariant();
    }

    /// <summary>Whether a normalised host is a DNS name or an IP address literal and nothing else.</summary>
    /// <param name="host">The host after <see cref="Normalize"/>.</param>
    /// <returns>True for a well-formed name or address.</returns>
    /// <remarks>
    /// An address literal is read by the same strict rule the panel reads a firewall range with: four plain decimal
    /// octets for IPv4, no scope id for IPv6, no <c>/</c>. A name needs two or more labels, so <c>localhost</c> and
    /// a bare label are not accepted.
    /// </remarks>
    public static bool IsValid(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > RemoteServerFieldLimits.HostMaxLength)
        {
            return false;
        }

        if (host.Contains(':', StringComparison.Ordinal) || IsDigitsAndDots(host))
        {
            return host.IndexOf('/', StringComparison.Ordinal) < 0 && CidrRule.TryParse(host, out _);
        }

        return HostNameRule.IsHostName(host);
    }

    /// <summary>Whether a text can only be an IPv4 literal: digits and dots.</summary>
    /// <param name="host">The text.</param>
    /// <returns>True when every character is a digit or a dot.</returns>
    private static bool IsDigitsAndDots(string host)
    {
        return host.All(character =>
        {
            return char.IsAsciiDigit(character) || character == '.';
        });
    }
}
