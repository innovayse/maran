using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>
/// The value each template variable takes for one zone, and the time to live a record without one gets. An
/// empty value means "not stated", and a record that needs it is skipped, never filled with an invented one.
/// </summary>
/// <param name="Apex">The zone's apex name.</param>
/// <param name="Ipv4">The public IPv4 address, or empty.</param>
/// <param name="Ipv6">The public IPv6 address, or empty.</param>
/// <param name="Ns1">The first name server, or empty.</param>
/// <param name="Ns2">The second name server, or empty.</param>
/// <param name="Hostmaster">The SOA mailbox in dotted form, or empty.</param>
/// <param name="DefaultTtl">The time to live of a template record that states none.</param>
public sealed record TemplateContext(
    string Apex,
    string Ipv4,
    string Ipv6,
    string Ns1,
    string Ns2,
    string Hostmaster,
    int DefaultTtl)
{
    /// <summary>
    /// The context a template is checked against when it is saved: documentation names and addresses
    /// (RFC 2606, RFC 5737, RFC 3849) that cannot be a real host, with every variable stated.
    /// </summary>
    public static readonly TemplateContext Sample = new(
        "example.com",
        "192.0.2.1",
        "2001:db8::1",
        "ns1.example.com",
        "ns2.example.com",
        "hostmaster.example.com",
        DnsLimits.DefaultTtl);

    /// <summary>Reads the context a zone is created in from the settings in force.</summary>
    /// <param name="apex">The zone's apex name.</param>
    /// <param name="settings">The settings: addresses, name servers, SOA mailbox and default time to live.</param>
    /// <returns>The context.</returns>
    public static TemplateContext For(string apex, DnsSettings settings)
    {
        return new TemplateContext(
            apex,
            settings.PublicIpv4,
            settings.PublicIpv6,
            settings.PrimaryNameserver,
            settings.SecondaryNameserver,
            settings.HostmasterMailbox,
            settings.DefaultTtl);
    }

    /// <summary>Tells what a variable stands for in this context.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>Its value, or the empty string when the context does not state it.</returns>
    public string ValueOf(TemplateVariable variable)
    {
        return variable switch
        {
            TemplateVariable.Apex => Apex,
            TemplateVariable.Ipv4 => Ipv4,
            TemplateVariable.Ipv6 => Ipv6,
            TemplateVariable.Ns1 => Ns1,
            TemplateVariable.Ns2 => Ns2,
            TemplateVariable.Hostmaster => Hostmaster,
            _ => throw new ArgumentOutOfRangeException(nameof(variable), variable, "Unknown template variable."),
        };
    }
}
