using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.GetResellerInvitationStatus;

/// <summary>
/// Asks where a reseller's login stands in its invitation: none issued, outstanding, lapsed, or
/// accepted.
/// </summary>
/// <param name="ResellerId">
/// The reseller, taken from the route rather than a query string. Guarded like every
/// server-established member of a command (rules/csharp.md "An endpoint binds its command
/// directly"): the guard is a property of the type, not of how today's action builds it.
/// </param>
public sealed record GetResellerInvitationStatusQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ResellerId);
