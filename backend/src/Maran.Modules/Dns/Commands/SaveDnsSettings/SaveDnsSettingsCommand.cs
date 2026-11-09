using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.SaveDnsSettings;

/// <summary>
/// Saves the installation's DNS settings. Existing zones keep the name servers they were last published
/// with until their next edit or the next start-up reconciliation.
/// </summary>
/// <param name="PrimaryNameserver">The first name server every zone names, a host name such as <c>ns1.example.com</c>.</param>
/// <param name="SecondaryNameserver">The second name server, or an empty string when the server has only one.</param>
/// <param name="HostmasterMailbox">The SOA mailbox in dotted form: <c>hostmaster.example.com</c> for <c>hostmaster@example.com</c>.</param>
/// <param name="PublicIpv4">The server's public IPv4 address, or an empty string.</param>
/// <param name="PublicIpv6">The server's public IPv6 address, or an empty string.</param>
/// <param name="DefaultTtl">The default time to live of a zone, in seconds.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record SaveDnsSettingsCommand(
    string PrimaryNameserver,
    string SecondaryNameserver,
    string HostmasterMailbox,
    string PublicIpv4,
    string PublicIpv6,
    int DefaultTtl,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
