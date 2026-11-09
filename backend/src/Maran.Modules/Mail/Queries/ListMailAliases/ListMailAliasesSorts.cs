using Maran.Modules.Mail.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailAliases;

/// <summary>
/// The orders the aliases list accepts. <see cref="ListMailAliasesQueryValidator"/> refuses any other sort
/// and <see cref="ListMailAliasesQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListMailAliasesSorts
{
    /// <summary>
    /// <see cref="MailAliasSortFields.Address"/> and <see cref="MailAliasSortFields.CreatedAt"/>, oldest first
    /// when none is named, with ties broken by the row id.
    /// </summary>
    /// <remarks>
    /// <c>address</c> orders by the whole address as the list shows it, local part then domain, and not by the
    /// local part alone: two domains of one account would otherwise interleave their <c>info</c> addresses.
    /// </remarks>
    public static readonly SortMap<MailAlias> Map = new SortMapBuilder<MailAlias>()
        .By(MailAliasSortFields.Address, alias => alias.LocalPart + "@" + alias.MailDomain.Name)
        .By(MailAliasSortFields.CreatedAt, alias => alias.CreatedAt)
        .DefaultTo(MailAliasSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(alias => alias.Id);
}
