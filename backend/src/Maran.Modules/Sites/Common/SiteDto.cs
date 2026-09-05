using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>Outward, list-shaped view of a <see cref="Site"/>.</summary>
/// <remarks>
/// Deliberately without the document root: a filesystem path is operator-facing diagnostic detail,
/// and a list is the widest-read surface the module has (rules/security.md — a customer's response
/// carries no paths). <see cref="SiteDetailDto"/> carries it for the single-site read.
/// </remarks>
/// <param name="Id">The site's identity.</param>
/// <param name="AccountId">The account that owns this site.</param>
/// <param name="Domain">The primary domain served by this site.</param>
/// <param name="BackendType">Which backend serves this site's content.</param>
/// <param name="PhpVersion">The bound PHP version, or the empty string when the backend is not PHP.</param>
/// <param name="Status">Whether the site serves its own content or a suspension response.</param>
/// <param name="CreatedAt">The instant the site was created.</param>
public sealed record SiteDto(
    Guid Id,
    Guid AccountId,
    string Domain,
    SiteBackendType BackendType,
    string PhpVersion,
    SiteStatus Status,
    DateTimeOffset CreatedAt);
