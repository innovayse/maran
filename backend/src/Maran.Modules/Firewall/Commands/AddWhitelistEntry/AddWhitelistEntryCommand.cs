using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Firewall.Commands.AddWhitelistEntry;

/// <summary>Adds an address range the panel's automatic bans will never touch.</summary>
/// <remarks>
/// Bound directly by <c>POST /api/v1/firewall/whitelist</c> — there is no separate request type
/// (rules/csharp.md "Server-established members of a command").
/// </remarks>
/// <param name="Cidr">The range in CIDR notation — <c>203.0.113.7/32</c>, <c>2001:db8::/32</c>.</param>
/// <param name="Note">What the range is, in the administrator's own words, for whoever reads it later.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body — an audited address a caller could
/// state is not evidence of anything.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record AddWhitelistEntryCommand(
    string Cidr,
    string Note,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
