using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>Outward, list-shaped view of an <see cref="Account"/>.</summary>
/// <param name="Id">The account's identity.</param>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="Status">The account's current lifecycle state.</param>
/// <param name="StatusName">The state as a reader reads it, localized for the request's culture.</param>
/// <param name="CreatedAt">The instant the account was created.</param>
/// <param name="ResellerId">The reseller that owns the account, or <c>null</c> for the administrator's own.</param>
/// <param name="ResellerName">
/// The owning reseller's name, or <c>null</c> for the administrator's own — and for an owner the
/// caller may not see, which a reseller never meets, since it is shown only its own accounts.
/// </param>
public sealed record AccountDto(
    Guid Id,
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    AccountStatus Status,
    string StatusName,
    DateTimeOffset CreatedAt,
    Guid? ResellerId,
    string? ResellerName);
