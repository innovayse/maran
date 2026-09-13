using System.Text.Json.Serialization;
using Maran.Agent.Client.Services.FirewallService;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Firewall.Commands.AllowPort;

/// <summary>
/// Opens a port on the host firewall, optionally scoped to one source range.
/// </summary>
/// <remarks>
/// The command carries no SSH port and no panel port. Those are host facts read from
/// <c>FirewallOptions</c> by the handler, never taken from the request: a caller able to name them
/// could name the wrong ones, and the agent renders the whole ruleset from what it is told.
/// </remarks>
/// <param name="Port">The port to allow, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range to allow from; <c>0.0.0.0/0</c> allows any source.</param>
/// <param name="PortTo">
/// The inclusive upper bound when the rule opens a RANGE of ports, 2-65535, or null for a single
/// port. Must be strictly above <c>Port</c>: an inverted pair is refused by <c>nft</c> and aborts
/// the whole ruleset load, and an equal pair is ACCEPTED by it — a second spelling of one port that
/// a later deny for that port would not match.
/// </param>
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
public sealed record AllowPortCommand(
    int Port,
    AgentFirewallProtocol Protocol,
    string SourceCidr,
    int? PortTo = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
