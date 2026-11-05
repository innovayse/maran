using System.Text.Json.Serialization;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sftp.Queries.ListSftpUserKeys;

/// <summary>
/// Reads one page of the public keys registered for one of the caller's SFTP logins, optionally
/// searched by label. Bound straight from the query string
/// (<c>GET /api/v1/sftp-users/{id}/keys?page=2&amp;search=laptop&amp;sort=-createdAt</c>);
/// <see cref="ListSftpUserKeysQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the login is read through the context's global query
/// filter, so another account's login answers 404 and its keys are never read.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the key's label, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// One of <see cref="SftpUserKeySortFields"/> either way (<c>label</c>, <c>-createdAt</c>), or
/// <see cref="SortRequest.Default"/> for oldest first.
/// </param>
/// <param name="SftpUserId">
/// Which login's keys to list. Established by the server from the route and stamped by the action;
/// never bound from the query string.
/// </param>
public sealed record ListSftpUserKeysQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SftpUserId = default) : IPagedQuery;
