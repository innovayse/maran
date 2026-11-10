namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>
/// The closed set of variables a zone template may name in a record's value. There is no expression
/// language: a variable is substituted as text and nothing else, so the set is the whole vocabulary.
/// </summary>
public enum TemplateVariable
{
    /// <summary>The zone's own apex name, such as <c>example.com</c>.</summary>
    Apex,

    /// <summary>The server's public IPv4 address from the DNS settings.</summary>
    Ipv4,

    /// <summary>The server's public IPv6 address from the DNS settings.</summary>
    Ipv6,

    /// <summary>The first name server from the DNS settings.</summary>
    Ns1,

    /// <summary>The second name server from the DNS settings, empty when the server has only one.</summary>
    Ns2,

    /// <summary>The SOA mailbox from the DNS settings, in dotted form.</summary>
    Hostmaster,
}
