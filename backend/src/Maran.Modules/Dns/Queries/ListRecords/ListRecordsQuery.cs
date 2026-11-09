using System.Text.Json.Serialization;
using Maran.Modules.Dns.Domain.Enums;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Queries.ListRecords;

/// <summary>
/// Reads one page of a zone's records, optionally searched and narrowed to one type. Bound from the query
/// string (<c>GET /api/v1/dns-zones/{zoneId}/records?type=Txt&amp;search=spf</c>).
/// </summary>
/// <remarks>
/// The zone is the ROUTE's and is established by the action; the zone is first looked up through the tenant
/// filter, so a zone another account owns answers "not found" and not an empty page.
/// </remarks>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the owner name or the value, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="ZoneRecordSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="Type">Only records of this type, or <c>null</c> for every type.</param>
/// <param name="ZoneId">The zone, from the route. Established by the server; never bound from the request.</param>
public sealed record ListRecordsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    RecordType? Type = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ZoneId = default) : IPagedQuery;
