using Maran.Modules.Identity.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>
/// The orders the sessions list accepts. <see cref="ListSessionsQueryValidator"/> refuses any other
/// sort and <see cref="ListSessionsQueryHandler"/> orders by exactly these, so the whitelist is written
/// once.
/// </summary>
public static class ListSessionsSorts
{
    /// <summary>
    /// <see cref="SessionSortFields.IssuedAt"/> and <see cref="SessionSortFields.ExpiresAt"/>, newest
    /// first when none is named — the order the list had before it was paged — with ties broken by the
    /// session id.
    /// </summary>
    public static readonly SortMap<Session> Map = new SortMapBuilder<Session>()
        .By(SessionSortFields.IssuedAt, session => session.IssuedAt)
        .By(SessionSortFields.ExpiresAt, session => session.ExpiresAt)
        .DefaultTo(SessionSortFields.IssuedAt, SortDirection.Descending)
        .TieBreaker(session => session.Id);
}
