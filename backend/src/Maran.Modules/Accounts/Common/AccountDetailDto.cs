using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// Outward, single-resource view of an <see cref="Account"/>: the list view's fields plus what only
/// the account's own page shows — its plan by name and its owner's contact address — kept as a
/// distinct type from <see cref="AccountDto"/> for exactly that reason (rules/csharp.md "DTO naming
/// and home").
/// </summary>
/// <param name="Id">The account's identity.</param>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="Status">The account's current lifecycle state.</param>
/// <param name="CreatedAt">The instant the account was created.</param>
/// <param name="PlanDisplayName">The plan's name, already in the request's language.</param>
/// <param name="OwnerEmail">
/// The account's own contact address, which its login's invitation and password resets follow;
/// <c>null</c> only for an account that predates the column (see <see cref="Account.OwnerEmail"/>).
/// </param>
public sealed record AccountDetailDto(
    Guid Id,
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    AccountStatus Status,
    DateTimeOffset CreatedAt,
    string PlanDisplayName,
    string? OwnerEmail);
