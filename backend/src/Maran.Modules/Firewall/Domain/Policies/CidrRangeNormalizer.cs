using System.Globalization;
using System.Net;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// The one place a range that arrives from a boundary with no human on it is put into the plain form
/// this module can act on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <see cref="CidrRange"/> instead of inside it.</b> The two types answer
/// the same question at two boundaries that differ in one decisive way. A range typed into the panel
/// arrives with an administrator waiting on the answer, so <see cref="CidrRange"/> REFUSES the
/// IPv4-mapped spelling and the 400 tells them to write the plain one. The installer's seed arrives
/// with nobody waiting: it was recorded months ago by a shell script into <c>panel.env</c>, and the
/// panel reads it at boot with no way to ask. Refusing there produces silence — an empty whitelist,
/// one warning line in a boot log, and an install transcript that already told the operator the
/// whitelist was seeded. So the seed is translated and the typed value is refused, and that is the
/// whole difference between the two types.
/// </para>
/// <para>
/// <b>What the translation is.</b> <c>::ffff:203.0.113.7/128</c> and <c>203.0.113.7/32</c> name the
/// same host, because <see cref="IpAddressNormalizer"/> turns every mapped address into plain IPv4
/// before anything is compared — so the mapped range would match nothing while the plain one matches
/// exactly what the operator meant. This is the spelling a dual-stack sshd reports in
/// <c>SSH_CLIENT</c> (<c>ListenAddress ::</c> with <c>net.ipv6.bindv6only=0</c>), which is how it
/// reaches the seed in the first place.
/// </para>
/// <para>
/// <b>The translation is exact, and it can be, which is why this is not the rewriting
/// <see cref="CidrRange"/> argues against.</b> A range shorter than the mapped block would cover
/// addresses outside it and equal no IPv4 range at all — but no such range can arrive: the block's
/// own sixteen bits are set, so a prefix below 96 leaves a host bit set and the parse refuses it
/// (measured on net9.0). Every mapped range that parses is exactly the IPv4 range this returns, so
/// nothing here picks between two readings of an operator's intent.
/// </para>
/// <para>
/// Everything that comes out of here has been through <see cref="CidrRange.IsUsable"/>, so a caller
/// storing the result cannot store something the rest of the module would refuse.
/// </para>
/// </remarks>
public static class CidrRangeNormalizer
{
    /// <summary>How many bits of an IPv4-mapped address are the mapping rather than the address.</summary>
    /// <remarks>
    /// <c>::ffff:0:0/96</c> is the whole mapped block, so a mapped range's IPv4 prefix length is its
    /// own prefix length less this — <c>/128</c> is one host, which is <c>/32</c> in IPv4.
    /// </remarks>
    private const int MappedPrefixBits = 96;

    /// <summary>What separates an IPv6 address from its scope id.</summary>
    private const char ScopeSeparator = '%';

    /// <summary>Puts <paramref name="cidr"/> into the plain form, if it has one.</summary>
    /// <param name="cidr">The candidate, in CIDR notation. Null and blank are answers, not faults.</param>
    /// <param name="normalized">
    /// The range in plain IPv4 or plain IPv6 form and in one canonical spelling, or the empty string
    /// when there is no such form.
    /// </param>
    /// <returns>False when the value is not a range this module can act on in any spelling.</returns>
    public static bool TryNormalize(string? cidr, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        // On the text and before the parse, for the reason CidrRange gives: the parse is what makes
        // a NAMED scope disappear, so a check afterwards would pass exactly what it exists to catch.
        if (cidr.Contains(ScopeSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IPNetwork.TryParse(cidr, out var network))
        {
            return false;
        }

        if (!network.BaseAddress.IsIPv4MappedToIPv6)
        {
            // Not passed through verbatim: the parsed range's own spelling, so the row an operator
            // reads back is the row that is matched.
            normalized = network.ToString();
            return CidrRange.IsUsable(normalized);
        }

        // No guard against a prefix shorter than the mapped block, because there cannot be one and a
        // check that cannot fail is decoration. Measured on net9.0: the mapped block's own sixteen
        // bits are all set, so any prefix below 96 leaves a host bit set and IPNetwork.TryParse has
        // already refused the value — `::ffff:0:0/95`, `::ffff:0:0/64` and `::ffff:203.0.113.0/119`
        // all come back false. Every mapped range that reaches this line therefore has 96 to 128
        // bits of prefix, and the subtraction below is an IPv4 prefix length of 0 to 32.
        //
        // Composed as text and re-parsed by the check below rather than built with the IPNetwork
        // constructor, which THROWS on an argument it dislikes: this method answers, it does not
        // fault, and its callers are a boot-time seeder with nobody to report an exception to.
        normalized = string.Create(
            CultureInfo.InvariantCulture,
            $"{Ipv4MappedAddress.Unwrap(network.BaseAddress)}/{network.PrefixLength - MappedPrefixBits}");

        return CidrRange.IsUsable(normalized);
    }
}
