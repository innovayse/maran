using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;

/// <summary>
/// Replaces a hosting account's owner address, and with it the address the account's login is mailed
/// at — its invitation and its password resets.
/// </summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/accounts/{id}/owner-email</c>. The account's id sits with the
/// server-established members at the end, because a guarded non-nullable parameter must carry a
/// default and a C# default is only legal in a trailing position.
/// </remarks>
/// <param name="OwnerEmail">The new owner address.</param>
/// <param name="AccountId">
/// The account being changed, established by the server from the route and stamped by the action.
/// Never bound from the request.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record ChangeAccountOwnerEmailCommand(
    string OwnerEmail,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
