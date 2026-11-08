namespace Maran.Modules.Dns.Common;

/// <summary>The installation's DNS settings as the administrator's screen shows them.</summary>
/// <param name="PrimaryNameserver">The first name server every zone names; empty until stated.</param>
/// <param name="SecondaryNameserver">The second name server, or empty.</param>
/// <param name="HostmasterMailbox">The SOA mailbox in dotted form.</param>
/// <param name="PublicIpv4">The server's public IPv4 address, or empty.</param>
/// <param name="PublicIpv6">The server's public IPv6 address, or empty.</param>
/// <param name="DefaultTtl">The default time to live of a zone, in seconds.</param>
/// <param name="Enabled">Whether the administrator asked for the name server to run.</param>
/// <param name="Complete">Whether the settings state what a zone cannot be published without.</param>
public sealed record DnsSettingsDto(
    string PrimaryNameserver,
    string SecondaryNameserver,
    string HostmasterMailbox,
    string PublicIpv4,
    string PublicIpv6,
    int DefaultTtl,
    bool Enabled,
    bool Complete);
