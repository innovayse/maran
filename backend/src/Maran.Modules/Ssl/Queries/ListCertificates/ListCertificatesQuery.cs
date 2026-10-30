using Maran.Modules.Ssl.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// Reads one page of the certificates the caller may see, optionally searched and narrowed to a site,
/// a status or the ones about to expire. Bound straight from the query string
/// (<c>GET /api/v1/certificates?siteId=…&amp;status=renewalFailing&amp;expiring=true</c>);
/// <see cref="ListCertificatesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// What a caller may see is never a member of this query: it is decided by the tenant filter on
/// <c>SslDbContext</c>, not by a parameter a caller could set. <paramref name="SiteId"/> only narrows
/// that scope further — a customer naming another account's site gets an empty page with a total of
/// zero, never that site's certificates.
/// </para>
/// <para>
/// The status filter is written in the camelCase the JSON answers carry (<c>renewalFailing</c>); the
/// binder parses an enum name ignoring case, and a number naming no member is refused by the validator.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the domain, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// One of <see cref="CertificateSortFields"/> either way (<c>domain</c>, <c>-notAfter</c>), or
/// <see cref="SortRequest.Default"/> for soonest expiry first.
/// </param>
/// <param name="SiteId">Only this site's certificates, or <c>null</c> for every site the caller may see.</param>
/// <param name="Status">
/// Only certificates in this <see cref="CertificateStatus"/> at the moment of the request, or
/// <c>null</c> for every status.
/// </param>
/// <param name="Expiring">
/// <c>true</c> for only the certificates that have not expired yet and expire within the renewal
/// window (<c>CertificateRenewalHandler.RenewalWindow</c>, thirty days) of the moment of the request —
/// the window in which the panel starts re-ordering, so "expiring" means the same thing on this list
/// as it does to renewal. <c>false</c>, the default, does not narrow.
/// </param>
public sealed record ListCertificatesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? SiteId = null,
    CertificateStatus? Status = null,
    bool Expiring = false) : IPagedQuery;
