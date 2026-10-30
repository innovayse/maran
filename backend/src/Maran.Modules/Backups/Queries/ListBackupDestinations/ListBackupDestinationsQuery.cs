using Maran.Modules.Backups.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>
/// Reads one page of the places this server records backups as living (spec §11), optionally
/// searched by name and narrowed to one kind. Bound straight from the query string
/// (<c>GET /api/v1/backup-destinations?search=local&amp;kind=local</c>);
/// <see cref="ListBackupDestinationsQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <remarks>
/// The list is small — one destination on this build — and is paged in memory by the kernel's
/// enumerable pager, so it speaks the same list contract as every other table without a second
/// paging implementation.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the destination's name — as the screen shows it, or as the row
/// stores it — or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="BackupDestinationSortFields"/> either way, or <see cref="SortRequest.Default"/>
/// for by name.
/// </param>
/// <param name="Kind">Only destinations of this kind, or <c>null</c> for every kind.</param>
public sealed record ListBackupDestinationsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    BackupDestinationKind? Kind = null) : IPagedQuery;
