using Maran.Modules.Backups.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// Reads one page of the backups the caller may see, optionally searched and narrowed to an account,
/// a status, a kind and a window of start times. Bound straight from the query string
/// (<c>GET /api/v1/backups?page=2&amp;search=shop&amp;status=failed&amp;from=2026-10-01T00:00:00Z</c>);
/// <see cref="ListBackupsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the search reads, and why not more.</b> A backup has no label of its own: its only text
/// is the owner's name — the live account's, or the username a deleted account left on the row — so
/// that is what a search matches. The id is a GUID nobody types, and the failure code is a machine
/// value the status filter already narrows by.
/// </para>
/// <para>
/// The enum filters are written on the query string in the camelCase the JSON answers carry
/// (<c>failed</c>, <c>preDeletion</c>); a number naming no member is refused while binding, with
/// <c>RequestParameterInvalid</c>.
/// </para>
/// <para>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to
/// the caller's own account (rules/security.md item 6). <paramref name="AccountId"/> only narrows that
/// scope further, and the two are combined with AND — a customer naming another account's id gets an
/// empty page with a total of zero, never that account's rows.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the owning account's name or a deleted account's recorded
/// username, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="BackupSortFields"/> either way (<c>size</c>, <c>-startedAt</c>), or
/// <see cref="SortRequest.Default"/> for newest first.
/// </param>
/// <param name="AccountId">Only this account's backups, or <c>null</c> for every account the caller may see.</param>
/// <param name="Status">Only backups in this state, or <c>null</c> for every state.</param>
/// <param name="Kind">Only backups taken for this reason, or <c>null</c> for every kind.</param>
/// <param name="From">Only backups started at or after this instant, or <c>null</c> for no lower bound.</param>
/// <param name="To">
/// Only backups started at or before this instant, or <c>null</c> for no upper bound. When both bounds
/// are given, <paramref name="From"/> must not be later than this.
/// </param>
public sealed record ListBackupsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    BackupStatus? Status = null,
    BackupKind? Kind = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IPagedQuery;
