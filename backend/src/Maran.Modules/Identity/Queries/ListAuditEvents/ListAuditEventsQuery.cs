using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// Reads one page of the audit journal, optionally searched and narrowed to one action, one outcome
/// and a window of instants. Bound straight from the query string
/// (<c>GET /api/v1/audit?page=2&amp;search=alice&amp;outcome=failed&amp;from=2026-10-01T00:00:00Z</c>);
/// <see cref="ListAuditEventsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the filters are, and why not more.</b> They are the columns the journal records: the
/// action, whether it worked, and when. There is no account filter because an entry records no
/// account — the journal outlives the accounts and users it describes, and a failed sign-in names a
/// user who never existed. There is no separate actor filter because the actor is free text the
/// search already reaches; a choice list of every name ever typed at the sign-in form would be a
/// list an attacker writes.
/// </para>
/// <para>
/// The journal is administrators-only and server-wide; the controller's policy decides who may read
/// it, and nothing here narrows by tenant.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the actor's name, the subject or the address the request came
/// from, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="AuditEventSortFields"/> either way (<c>actor</c>, <c>-occurredAt</c>), or
/// <see cref="SortRequest.Default"/> for newest first.
/// </param>
/// <param name="Action">
/// Only entries recording this machine-stable action (<c>LoginFailed</c>), or
/// <see cref="FilterValue.None"/> for every action.
/// </param>
/// <param name="Outcome">Only entries that ended this way, or <c>null</c> for both.</param>
/// <param name="From">Only entries at or after this instant, or <c>null</c> for no lower bound.</param>
/// <param name="To">
/// Only entries at or before this instant, or <c>null</c> for no upper bound. When both bounds are
/// given, <paramref name="From"/> must not be later than this.
/// </param>
public sealed record ListAuditEventsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue Action = default,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IPagedQuery;
