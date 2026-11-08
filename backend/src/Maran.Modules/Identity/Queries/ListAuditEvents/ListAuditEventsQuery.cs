using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// Reads one page of the audit journal, optionally searched and narrowed to one action, one outcome,
/// a window of instants, one actor and one hosting account. Bound straight from the query string
/// (<c>GET /api/v1/audit?page=2&amp;search=alice&amp;outcome=failed&amp;from=2026-10-01T00:00:00Z</c>);
/// <see cref="ListAuditEventsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the filters are, and why not more.</b> They are the columns the journal records: the
/// action, whether it worked, when, who did it and which hosting account it concerns. The actor is a
/// typed text filter (an exact name), not a choice list: a list of every name ever typed at the
/// sign-in form would be a list an attacker writes. The account filter reaches only the entries that
/// were stamped with an account — customer sessions, viewed-as sessions and the Accounts module's own
/// entries; an administrator's entry from another module names the account only in its subject, which
/// the search reaches. The journal outlives the accounts and users it describes, and a failed sign-in
/// names a user who never existed.
/// </para>
/// <para>
/// The same query serves two screens, and what narrows it is the CALLER, never a member of this record:
/// an administrator reads the whole journal, and a reseller only the entries about the accounts it owns
/// and its own login's (issue #370), so a reseller's <paramref name="Account"/> must be one of its own
/// or the answer is a 404. The controllers' policies decide who may ask; <c>AuditEventScopeReader</c>
/// decides what they see.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the actor's name, the subject, the address the request came
/// from or the name of the operator who was viewing as the actor, or <see cref="SearchTerm.None"/>.
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
/// <param name="Actor">
/// Only entries whose actor used exactly this name, compared without regard to case, or
/// <see cref="FilterValue.None"/> for every actor.
/// </param>
/// <param name="Account">Only entries recorded against this hosting account, or <c>null</c> for every entry.</param>
public sealed record ListAuditEventsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue Action = default,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    FilterValue Actor = default,
    Guid? Account = null) : IPagedQuery;
