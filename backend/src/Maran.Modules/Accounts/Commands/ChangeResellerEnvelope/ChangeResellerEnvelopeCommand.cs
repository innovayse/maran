using System.Text.Json.Serialization;
using Maran.Modules.Accounts.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;

/// <summary>Replaces what an administrator sold a reseller.</summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/resellers/{id}/envelope</c>. The reseller's id sits with the
/// server-established members at the end, because a guarded non-nullable parameter must carry a default
/// and a C# default is only legal in a trailing position.
/// </remarks>
/// <param name="Envelope">
/// The new envelope. It may sit below what the reseller already owns: existing plans and accounts keep
/// working and only new writes are refused.
/// </param>
/// <param name="ResellerId">
/// The reseller whose envelope changes, established by the server from the route and stamped by the
/// action. Never bound from the request — a body id could disagree with the path id, and one request
/// would then name two resellers.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record ChangeResellerEnvelopeCommand(
    ResellerEnvelopeDto Envelope,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ResellerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
