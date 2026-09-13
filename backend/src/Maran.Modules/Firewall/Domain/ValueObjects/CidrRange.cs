using System.Net;

namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// The one place this module decides whether a string is an address range it can act on.
/// </summary>
/// <remarks>
/// <para>
/// It is <see cref="IPNetwork"/>'s own parse plus one refusal that type does not make: a range
/// carrying an IPv6 scope id. The refusal happens BEFORE the parse, and the reason is measured
/// rather than assumed — <c>IPNetwork.TryParse</c> accepts <c>fe80::1%3/128</c> and keeps the scope,
/// and accepts <c>fe80::1%eth0/128</c> while SILENTLY DROPPING it. A row written in the second form
/// would be stored and shown to an administrator as one range and matched as a different one, which
/// is the kind of quiet disagreement an exemption list must not contain.
/// </para>
/// <para>
/// It is also the form this module refuses at its other end: <see cref="Maran.Modules.Firewall.Domain.Policies.IpAddressNormalizer"/>
/// STRIPS the scope from every address it normalises, so a scoped range could only ever be
/// compared against addresses that have none. A list that cannot match is worse than an empty one — an administrator
/// reading it back believes they cannot be banned, and the row stops them adding one that works.
/// </para>
/// <para>
/// The IPv4-mapped form (<c>::ffff:198.51.100.10/128</c>) is refused for the SAME reason, and it
/// took a review to notice that the reason applied to it too. <see cref="Maran.Modules.Firewall.Domain.Policies.IpAddressNormalizer"/>
/// turns every mapped address into plain IPv4 before anything is compared, so a mapped RANGE stays
/// in the IPv6 family and <c>IPNetwork.Contains</c> is false across families — measured on net9.0:
/// <c>TryParse("::ffff:203.0.113.7/128")</c> is true while <c>Contains("203.0.113.7")</c> is false.
/// The row was therefore accepted, returned to the administrator verbatim, and exempted nobody:
/// they read their own address in the whitelist and were banned by the next refused sign-ins.
/// </para>
/// <para>
/// Refused rather than rewritten to <c>198.51.100.10/32</c>, which would also have worked, because
/// this type answers for the boundary where a person is waiting: a 400 tells them to write the plain
/// form, and rewriting decides for them. That argument is about the HTTP boundary and does not reach
/// the installer's seed, which nobody is waiting on — there
/// <see cref="Maran.Modules.Firewall.Domain.Policies.CidrRangeNormalizer"/> translates the same spelling instead, and says why.
/// </para>
/// <para>
/// <b>What this does NOT refuse.</b> The deprecated IPv4-COMPATIBLE form (<c>::198.51.100.10/128</c>,
/// RFC 4291) is accepted: it is not a mapped address, nothing in this system emits it, and a peer
/// whose stack genuinely used one would be matched by it — so refusing would remove a row that can
/// work rather than one that cannot. It is named here because the summary above claims to be the one
/// place this decision is made, and a reader is owed the edge it leaves open.
/// </para>
/// </remarks>
public static class CidrRange
{
    /// <summary>What separates an IPv6 address from its scope id.</summary>
    private const char ScopeSeparator = '%';

    /// <summary>Whether <paramref name="cidr"/> is a range this module will store or send.</summary>
    /// <param name="cidr">The candidate, in CIDR notation. Null and blank are answers, not faults.</param>
    /// <returns>
    /// False when it is absent, cannot be parsed, carries host bits beyond its prefix, names a
    /// scope, or is written in the IPv4-mapped form.
    /// </returns>
    /// <remarks>
    /// Null and blank answer false rather than throwing. FluentValidation 12.1.1 runs a
    /// <c>.Must(...)</c> even after the <c>.NotEmpty()</c> before it has already failed, so all
    /// three call sites reached here with a missing field and turned a 400 into a 500.
    /// </remarks>
    public static bool IsUsable(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        // Checked on the text, not on the parsed value: the parse is what makes a named scope
        // disappear, so a check afterwards would pass exactly the input this refusal exists for.
        if (cidr.Contains(ScopeSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IPNetwork.TryParse(cidr, out var network))
        {
            return false;
        }

        // Checked on the PARSED base address, unlike the scope id above: the mapped form has several
        // spellings (`::ffff:198.51.100.10`, `::ffff:c633:640a`) and only the parse tells them apart.
        return !network.BaseAddress.IsIPv4MappedToIPv6;
    }

    /// <summary>The one spelling of <paramref name="cidr"/> this module stores and shows.</summary>
    /// <param name="cidr">A range that has already passed <see cref="IsUsable"/>.</param>
    /// <returns>
    /// The parsed range written back out, or <paramref name="cidr"/> unchanged when it cannot be
    /// parsed at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// One range has many spellings that all parse to the same network — <c>0x7f.0.0.0/8</c> is
    /// <c>127.0.0.0/8</c>, <c>203.0.113.0/024</c> is <c>203.0.113.0/24</c> — and a row stored as
    /// written is then shown to an administrator as one range and matched as another. That is the
    /// same disagreement this type refuses a scope id and a mapped range for, and it costs one
    /// expression to close. It also makes the unique index on the column mean what it says: two
    /// spellings of one range were two rows, so removing one left the exemption in place while the
    /// screen said it had gone.
    /// </para>
    /// <para>
    /// Unparseable input is returned unchanged rather than refused, because this method's job is
    /// spelling and not validation: <see cref="IsUsable"/> is what says yes or no, at the boundary,
    /// and a value that reached here without passing it must not silently become a different one.
    /// </para>
    /// </remarks>
    public static string Canonical(string cidr)
    {
        return IPNetwork.TryParse(cidr, out var network) ? network.ToString() : cidr;
    }
}
