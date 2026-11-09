using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Outward view of one site's options, with the bounds and the choices its screen needs.
/// </summary>
/// <remarks>
/// Basic-authentication users are listed by name only: the panel stores a hash of each password and
/// never returns it, so an editor can keep, replace or remove a user but never read a credential.
/// </remarks>
/// <param name="SiteId">The site the options belong to.</param>
/// <param name="CanonicalHost">Which of the apex / "www." pair is canonical.</param>
/// <param name="ClientMaxBodySizeMib">The largest request body in MiB, or null for the web server's default.</param>
/// <param name="StaticCacheDays">How many days browsers may cache static files, or null for no cache headers.</param>
/// <param name="Redirects">The redirect rules, in the order they are rendered.</param>
/// <param name="BasicAuthUsers">The names of the basic-authentication users; empty means no authentication.</param>
/// <param name="IpAccessMode">Whether <paramref name="IpRules"/> allows or refuses; null exactly when there are no rules.</param>
/// <param name="IpRules">The addresses or CIDR ranges the access mode applies to.</param>
/// <param name="RequestRate">The request limit, or null when the site is not limited.</param>
/// <param name="Limits">The bounds a replacement must respect.</param>
/// <param name="Choices">The closed sets a replacement selects from.</param>
public sealed record SiteOptionsDto(
    Guid SiteId,
    CanonicalHost CanonicalHost,
    int? ClientMaxBodySizeMib,
    int? StaticCacheDays,
    IReadOnlyList<SiteRedirectDto> Redirects,
    IReadOnlyList<string> BasicAuthUsers,
    IpAccessMode? IpAccessMode,
    IReadOnlyList<string> IpRules,
    SiteRequestRateDto? RequestRate,
    SiteOptionsLimitsDto Limits,
    SiteOptionsChoicesDto Choices);
