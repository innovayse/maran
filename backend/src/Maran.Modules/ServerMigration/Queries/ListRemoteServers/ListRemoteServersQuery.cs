using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListRemoteServers;

/// <summary>
/// Reads one page of the registered remote servers, optionally searched by label or address and narrowed to one
/// state. Bound straight from the query string
/// (<c>GET /api/v1/remote-servers?search=old&amp;state=Ready&amp;sort=-lastTestedAt</c>);
/// <see cref="ListRemoteServersQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the label or the address, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="RemoteServerSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="State">
/// Only servers in this state, spelled as the state's name (<c>Ready</c>), or <see cref="FilterValue.None"/> for every
/// state. A spelling that names no state matches no server.
/// </param>
public sealed record ListRemoteServersQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue State = default) : IPagedQuery;
