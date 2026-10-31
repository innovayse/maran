using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>
/// Reads one page of one user's live sessions, optionally searched. Bound from the query string
/// (<c>GET /api/v1/sessions?page=2&amp;search=firefox&amp;sort=-expiresAt</c>);
/// <see cref="ListSessionsQueryValidator"/> holds the paging members to their bounds before the
/// handler runs.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the address or the user agent a session signed in with, or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="SessionSortFields"/> either way (<c>expiresAt</c>, <c>-issuedAt</c>), or
/// <see cref="SortRequest.Default"/> for the newest first.
/// </param>
/// <param name="UserId">
/// Whose sessions to list. Server-established: written by the controller from the caller's own token
/// and guarded against every binding source (rules/csharp.md "Server-established members of a
/// command"), so there is no parameter with which to ask for somebody else's devices.
/// </param>
/// <param name="CurrentSessionId">
/// The caller's own session, marked in the result. Server-established for the same reason: it comes
/// from the token's <c>sid</c> claim, never from the request.
/// </param>
public sealed record ListSessionsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid CurrentSessionId = default) : IPagedQuery;
