using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.GetInvitationStatus;

/// <summary>
/// Asks where a hosting account's owner stands in their invitation: none issued, outstanding,
/// lapsed, or accepted.
/// </summary>
/// <param name="AccountId">
/// The hosting account, taken from the route rather than a query string. Guarded like every
/// server-established member of a command (rules/csharp.md "An endpoint binds its command
/// directly"): the guard is a property of the type, not of how today's action builds it.
/// </param>
public sealed record GetInvitationStatusQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId);
