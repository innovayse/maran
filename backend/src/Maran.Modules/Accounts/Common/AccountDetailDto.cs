using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// Outward, single-resource view of an <see cref="Account"/>. Carries the same fields as
/// <see cref="AccountDto"/> today; kept as a distinct type because a detail view is expected to
/// grow fields (usage, linked sites) that the list view never needs (rules/csharp.md "DTO naming
/// and home").
/// </summary>
/// <param name="Id">The account's identity.</param>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="Status">The account's current lifecycle state.</param>
/// <param name="CreatedAt">The instant the account was created.</param>
public sealed record AccountDetailDto(
    Guid Id,
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    AccountStatus Status,
    DateTimeOffset CreatedAt);
