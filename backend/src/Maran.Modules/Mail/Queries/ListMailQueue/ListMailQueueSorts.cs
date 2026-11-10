using Maran.Modules.Mail.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// The orders the mail queue list accepts. <see cref="ListMailQueueQueryValidator"/> refuses any other
/// sort and <see cref="ListMailQueueQueryHandler"/> orders by exactly these, so the whitelist is
/// written once.
/// </summary>
public static class ListMailQueueSorts
{
    /// <summary>
    /// <see cref="MailQueueSortFields.ArrivedAt"/>, <see cref="MailQueueSortFields.Sender"/>,
    /// <see cref="MailQueueSortFields.Recipients"/> and <see cref="MailQueueSortFields.Size"/>: newest
    /// first when none is named, with ties broken by the queue id.
    /// </summary>
    /// <remarks>
    /// Over the wire row because the whole listing is read from the agent and paged in memory; there is
    /// no stored row to order.
    /// </remarks>
    public static readonly SortMap<MailQueueEntryDto> Map = new SortMapBuilder<MailQueueEntryDto>()
        .By(MailQueueSortFields.ArrivedAt, entry => entry.ArrivedAt)
        .By(MailQueueSortFields.Sender, entry => entry.Sender)
        .By(MailQueueSortFields.Recipients, entry => entry.RecipientCount)
        .By(MailQueueSortFields.Size, entry => entry.SizeBytes)
        .DefaultTo(MailQueueSortFields.ArrivedAt, SortDirection.Descending)
        .TieBreaker(entry => entry.QueueId);
}
