using Maran.Modules.Identity.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// The orders the audit journal accepts. <see cref="ListAuditEventsQueryValidator"/> refuses any other
/// sort and <see cref="ListAuditEventsQueryHandler"/> orders by exactly these, so the whitelist is
/// written once.
/// </summary>
public static class ListAuditEventsSorts
{
    /// <summary>
    /// <see cref="AuditEventSortFields.OccurredAt"/>, <see cref="AuditEventSortFields.Actor"/> and
    /// <see cref="AuditEventSortFields.Action"/>, newest first when none is named — the order the
    /// journal had before it was paged, and the one an operator asking "what just happened" needs —
    /// with ties broken by the entry id.
    /// </summary>
    /// <remarks>
    /// The action sorts by the machine constant, not by its localized name: the name is resolved per
    /// request after the page is read, and no SQL order can follow it.
    /// </remarks>
    public static readonly SortMap<AuditEvent> Map = new SortMapBuilder<AuditEvent>()
        .By(AuditEventSortFields.OccurredAt, auditEvent => auditEvent.OccurredAt)
        .By(AuditEventSortFields.Actor, auditEvent => auditEvent.ActorUsername)
        .By(AuditEventSortFields.Action, auditEvent => auditEvent.Action)
        .DefaultTo(AuditEventSortFields.OccurredAt, SortDirection.Descending)
        .TieBreaker(auditEvent => auditEvent.Id);
}
