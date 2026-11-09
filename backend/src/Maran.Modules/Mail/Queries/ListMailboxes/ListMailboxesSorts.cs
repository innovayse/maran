using Maran.Modules.Mail.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailboxes;

/// <summary>
/// The orders the mailboxes list accepts. <see cref="ListMailboxesQueryValidator"/> refuses any other sort
/// and <see cref="ListMailboxesQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListMailboxesSorts
{
    /// <summary>
    /// <see cref="MailboxSortFields.Address"/>, <see cref="MailboxSortFields.CreatedAt"/> and
    /// <see cref="MailboxSortFields.QuotaMib"/>, oldest first when none is named, with ties broken by the row id.
    /// </summary>
    /// <remarks>
    /// <c>address</c> orders by the address as the list shows it (local part, <c>@</c>, domain name), so the
    /// column a reader sorts is the column the rows are ordered by.
    /// </remarks>
    public static readonly SortMap<Mailbox> Map = new SortMapBuilder<Mailbox>()
        .By(MailboxSortFields.Address, mailbox => mailbox.LocalPart + "@" + mailbox.MailDomain.Name)
        .By(MailboxSortFields.CreatedAt, mailbox => mailbox.CreatedAt)
        .By(MailboxSortFields.QuotaMib, mailbox => mailbox.QuotaMib)
        .DefaultTo(MailboxSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(mailbox => mailbox.Id);
}
