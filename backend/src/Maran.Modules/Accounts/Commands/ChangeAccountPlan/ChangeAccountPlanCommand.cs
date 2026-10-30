using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.ChangeAccountPlan;

/// <summary>Moves one hosting account to another plan, and its disk quota on the host with it.</summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/accounts/{id}/plan</c>. The account's id sits with the
/// server-established members at the end, because a guarded non-nullable parameter must carry a
/// default and a C# default is only legal in a trailing position.
/// </remarks>
/// <param name="PlanId">The plan the account is to be sold from now on.</param>
/// <param name="AccountId">
/// The account being moved, established by the server from the route and stamped by the action.
/// Never bound from the request — a body id could disagree with the path id, and one request would
/// then name two accounts.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record ChangeAccountPlanCommand(
    Guid PlanId,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
