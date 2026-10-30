using Maran.Modules.Accounts.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>
/// The orders the accounts list accepts. <see cref="ListAccountsQueryValidator"/> refuses any other
/// sort and <see cref="ListAccountsQueryHandler"/> orders by exactly these, so the whitelist is
/// written once.
/// </summary>
public static class ListAccountsSorts
{
    /// <summary>
    /// <see cref="AccountSortFields.Name"/>, <see cref="AccountSortFields.CreatedAt"/> and
    /// <see cref="AccountSortFields.Status"/>, oldest first when none is named — the order the list
    /// had before it was paged — with ties broken by the account id.
    /// </summary>
    /// <remarks>
    /// The status is stored as its member name (<c>HasConversion&lt;string&gt;</c>), so PostgreSQL
    /// orders it alphabetically by that name, not by the enum's numeric value.
    /// </remarks>
    public static readonly SortMap<Account> Map = new SortMapBuilder<Account>()
        .By(AccountSortFields.Name, account => account.Name)
        .By(AccountSortFields.CreatedAt, account => account.CreatedAt)
        .By(AccountSortFields.Status, account => account.Status)
        .DefaultTo(AccountSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(account => account.Id);
}
