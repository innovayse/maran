using Maran.Modules.Accounts.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListAccountChoices;

/// <summary>The orders the account picker accepts, read by its validator and its handler alike.</summary>
public static class ListAccountChoicesSorts
{
    /// <summary>
    /// <see cref="AccountChoiceSortFields.Name"/> only, alphabetical by default — a picker is scanned
    /// by name — ties broken by id.
    /// </summary>
    public static readonly SortMap<Account> Map = new SortMapBuilder<Account>()
        .By(AccountChoiceSortFields.Name, account => account.Name)
        .DefaultTo(AccountChoiceSortFields.Name, SortDirection.Ascending)
        .TieBreaker(account => account.Id);
}
