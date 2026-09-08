using System.Text.Json.Serialization;
using Maran.Agent.Client.Services.FirewallService;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Firewall.Commands.DenyPort;

/// <summary>
/// Removes an allow from the host firewall, matching the source range the allow was scoped to.
/// </summary>
/// <remarks>
/// The source range is part of what identifies the rule, not decoration: a port allowed from one
/// office and from a monitoring probe is two rules, and a deny that ignored the range would remove
/// whichever the firewall happened to list first.
/// </remarks>
/// <param name="Port">The port to stop allowing, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range the original allow was scoped to.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request — an audited address a caller could state is
/// not evidence of anything. The guard is spelled with three attributes and each closes a different
/// door: <c>[JsonIgnore]</c> stops a JSON body (an input formatter, which never consults model
/// binding), <c>[property: BindNever]</c> stops a property-set binder, and the PARAMETER-target
/// <c>[BindNever]</c> stops the constructor binder — which is the one that runs for a positional
/// record bound from the QUERY STRING, as this command is. Measured: with the property-target
/// attribute alone, <c>?ipAddress=6.6.6.6</c> still reached the member.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request.</param>
public sealed record DenyPortCommand(
    int Port,
    AgentFirewallProtocol Protocol,
    string SourceCidr,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
