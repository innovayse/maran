using System.Net;
using System.Net.Sockets;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// One address range written as <c>address/prefix</c>, parsed strictly, and the question "is this
/// address inside it".
/// </summary>
/// <remarks>
/// <para>
/// A wrong parser is a wrong allow-list, so parsing refuses everything it cannot read exactly:
/// surrounding whitespace, a scope id (<c>%eth0</c>), a short IPv4 form (<c>10.1</c>), an octet with
/// a leading zero or outside 0-255, a prefix that is not plain decimal digits or exceeds the
/// family's width, and a <b>network address with host bits set</b> (<c>10.0.0.5/24</c>). Host bits
/// are REFUSED, not normalised: the text an administrator typed would otherwise mean something
/// other than what it says, and a silent rewrite of an allow-list is how a rule ends up broader
/// than its author thought.
/// </para>
/// <para>
/// A bare address is a single host: <c>/32</c> for IPv4, <c>/128</c> for IPv6. A rule written in the
/// IPv4-mapped IPv6 form (<c>::ffff:10.0.0.0/104</c>) is unwrapped to the IPv4 rule it means
/// (<c>10.0.0.0/8</c>), through <see cref="Ipv4MappedAddress"/>; a mapped rule whose prefix is
/// shorter than 96 would cover addresses that are not IPv4 and is refused. <see cref="Contains"/>
/// unwraps the candidate the same way, so a dual-stack listener's <c>::ffff:a.b.c.d</c> matches an
/// IPv4 rule. A family mismatch is never a match. A <c>/0</c> rule is accepted here (it is a valid
/// range that matches every address of its family); a caller that must refuse it checks
/// <see cref="PrefixLength"/>.
/// </para>
/// <para>
/// Any bound on how MANY rules a list may hold belongs to the module that stores the list, not here.
/// </para>
/// </remarks>
public sealed class CidrRule
{
    /// <summary>Width of an IPv4 address, in bits.</summary>
    private const int Ipv4Bits = 32;

    /// <summary>Width of an IPv6 address, in bits.</summary>
    private const int Ipv6Bits = 128;

    /// <summary>Bits an IPv4-mapped IPv6 address spends on its fixed <c>::ffff:</c> head.</summary>
    private const int MappedHeadBits = 96;

    /// <summary>The network address, as bytes (4 or 16), with every host bit zero.</summary>
    private readonly byte[] _network;

    /// <summary>The network address, in the plain family the rule applies to.</summary>
    public IPAddress Network { get; }

    /// <summary>The number of leading bits that must match, in the rule's own family.</summary>
    public int PrefixLength { get; }

    /// <summary>Builds a rule from parts <see cref="TryParse"/> has already validated.</summary>
    /// <param name="network">The network address with host bits zero.</param>
    /// <param name="prefixLength">The prefix length, within the family's width.</param>
    private CidrRule(IPAddress network, int prefixLength)
    {
        Network = network;
        PrefixLength = prefixLength;
        _network = network.GetAddressBytes();
    }

    /// <summary>Parses a rule written as <c>address/prefix</c> or as a bare address. Never throws.</summary>
    /// <param name="text">The text to read; null or anything unreadable is refused.</param>
    /// <param name="rule">The parsed rule; null when the text was refused.</param>
    /// <returns>True when the text is a strictly valid rule.</returns>
    public static bool TryParse(string? text, out CidrRule? rule)
    {
        rule = null;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var slash = text.IndexOf('/');
        var addressText = slash < 0 ? text : text.Substring(0, slash);
        var prefixText = slash < 0 ? null : text.Substring(slash + 1);

        if (!TryParseAddress(addressText, out var address))
        {
            return false;
        }

        var width = address.AddressFamily == AddressFamily.InterNetwork ? Ipv4Bits : Ipv6Bits;
        var prefix = width;

        if (prefixText is not null && !TryParsePrefix(prefixText, width, out prefix))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            if (prefix < MappedHeadBits)
            {
                return false;
            }

            address = Ipv4MappedAddress.Unwrap(address);
            prefix -= MappedHeadBits;
        }

        var bytes = address.GetAddressBytes();

        if (!HostBitsAreZero(bytes, prefix))
        {
            return false;
        }

        rule = new CidrRule(address, prefix);
        return true;
    }

    /// <summary>Whether an address lies inside this range.</summary>
    /// <param name="candidate">The address; an IPv4-mapped IPv6 form is read as its IPv4 address.</param>
    /// <returns>True when the families agree and the leading <see cref="PrefixLength"/> bits match.</returns>
    public bool Contains(IPAddress candidate)
    {
        var plain = Ipv4MappedAddress.Unwrap(candidate);

        if (plain.AddressFamily != Network.AddressFamily)
        {
            return false;
        }

        var bytes = plain.GetAddressBytes();
        var fullBytes = PrefixLength / 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (bytes[i] != _network[i])
            {
                return false;
            }
        }

        var remainder = PrefixLength % 8;

        if (remainder == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainder));
        return (bytes[fullBytes] & mask) == _network[fullBytes];
    }

    /// <summary>The rule as text: <c>address/prefix</c> in the plain family.</summary>
    /// <returns>The canonical spelling that <see cref="TryParse"/> reads back to an equal rule.</returns>
    public override string ToString()
    {
        return Network + "/" + PrefixLength;
    }

    /// <summary>Reads an address strictly; IPv4 must be four plain decimal octets.</summary>
    /// <param name="text">The address part of the rule.</param>
    /// <param name="address">The address when it was read.</param>
    /// <returns>True for a well-formed IPv4 or IPv6 address with no scope id.</returns>
    private static bool TryParseAddress(string text, out IPAddress address)
    {
        address = IPAddress.None;

        if (text.Contains(':'))
        {
            foreach (var c in text)
            {
                if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F' or ':' or '.'))
                {
                    return false;
                }
            }

            if (!IPAddress.TryParse(text, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            address = v6;
            return true;
        }

        var octets = text.Split('.');

        if (octets.Length != 4)
        {
            return false;
        }

        foreach (var octet in octets)
        {
            if (!IsPlainDecimal(octet, 3) || (octet.Length > 1 && octet[0] == '0') || int.Parse(octet) > 255)
            {
                return false;
            }
        }

        if (!IPAddress.TryParse(text, out var v4))
        {
            return false;
        }

        address = v4;
        return true;
    }

    /// <summary>Reads a prefix length: plain digits, no sign, no leading zero, within the width.</summary>
    /// <param name="text">The text after the slash.</param>
    /// <param name="width">The family's width in bits.</param>
    /// <param name="prefix">The prefix length when it was read.</param>
    /// <returns>True for a decimal number from 0 to <paramref name="width"/>.</returns>
    private static bool TryParsePrefix(string text, int width, out int prefix)
    {
        prefix = 0;

        if (!IsPlainDecimal(text, 3) || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        prefix = int.Parse(text);
        return prefix <= width;
    }

    /// <summary>Whether text is 1 to <paramref name="maxLength"/> ASCII digits and nothing else.</summary>
    /// <param name="text">The text to judge.</param>
    /// <param name="maxLength">The longest accepted length.</param>
    /// <returns>True for a short run of ASCII digits.</returns>
    private static bool IsPlainDecimal(string text, int maxLength)
    {
        if (text.Length == 0 || text.Length > maxLength)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every bit after the first <paramref name="prefix"/> bits is zero.</summary>
    /// <param name="bytes">The address bytes.</param>
    /// <param name="prefix">How many leading bits belong to the network.</param>
    /// <returns>True when no host bit is set.</returns>
    private static bool HostBitsAreZero(byte[] bytes, int prefix)
    {
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
        {
            if ((bytes[bit / 8] & (0x80 >> (bit % 8))) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
