using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>Outward view of one <see cref="Site"/>, with the fields a site's own screen needs.</summary>
/// <param name="Id">The site's identity.</param>
/// <param name="AccountId">The account that owns this site.</param>
/// <param name="Domain">The primary domain served by this site.</param>
/// <param name="Aliases">Additional hostnames answered by the same vhost.</param>
/// <param name="BackendType">Which backend serves this site's content.</param>
/// <param name="PhpVersion">The bound PHP version, or the empty string when the backend is not PHP.</param>
/// <param name="ProxyUpstream">The upstream forwarded to, or the empty string when the backend is not a proxy.</param>
/// <param name="DocumentRoot">The absolute document root the agent allocated.</param>
/// <param name="HasCertificate">Whether a TLS certificate is currently installed for this site.</param>
/// <param name="Status">Whether the site serves its own content or a suspension response.</param>
/// <param name="CreatedAt">The instant the site was created.</param>
public sealed record SiteDetailDto(
    Guid Id,
    Guid AccountId,
    string Domain,
    IReadOnlyList<string> Aliases,
    SiteBackendType BackendType,
    string PhpVersion,
    string ProxyUpstream,
    string DocumentRoot,
    bool HasCertificate,
    SiteStatus Status,
    DateTimeOffset CreatedAt);
