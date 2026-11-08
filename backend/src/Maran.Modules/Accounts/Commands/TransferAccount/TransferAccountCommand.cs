using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.TransferAccount;

/// <summary>
/// Hands one hosting account to another owner: a reseller, or the administrator, together with the plan
/// of that owner it is sold from from now on.
/// </summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/accounts/{id}/owner</c>, which only an administrator may call. The
/// account's id sits with the server-established members at the end, because a guarded non-nullable
/// parameter must carry a default and a C# default is only legal in a trailing position.
/// </remarks>
/// <param name="ResellerId">
/// The reseller that is to own the account; <c>null</c> hands it to the administrator. A field left out
/// of the body is <c>null</c> too, the same reading <c>CreateAccountCommand.ResellerId</c> has.
/// </param>
/// <param name="PlanId">
/// The plan the account is sold from afterwards. It must belong to the same owner as
/// <paramref name="ResellerId"/>: an account and its plan always share one, so the move names both.
/// </param>
/// <param name="AccountId">
/// The account being moved, established by the server from the route and stamped by the action. Never
/// bound from the request — a body id could disagree with the path id, and one request would then name
/// two accounts.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record TransferAccountCommand(
    Guid? ResellerId,
    Guid PlanId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
