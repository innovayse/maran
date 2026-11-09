using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>
/// The whole set of a site's options as one replacement states it. A site's options are replaced as a
/// unit, never edited field by field, so this is the one shape that crosses into the entity.
/// </summary>
/// <param name="CanonicalHost">Which of the apex / "www." pair is canonical.</param>
/// <param name="ClientMaxBodySizeMib">The largest request body in MiB, or null for the web server's default.</param>
/// <param name="StaticCacheDays">How many days browsers may cache static files, or null for no cache headers.</param>
/// <param name="Redirects">The redirect rules, in the order they are rendered.</param>
/// <param name="BasicAuthUsers">The basic-authentication users; empty means no authentication.</param>
/// <param name="IpAccessMode">Whether <paramref name="IpRules"/> allows or refuses; null exactly when there are no rules.</param>
/// <param name="IpRules">The addresses or CIDR ranges, normalised.</param>
/// <param name="RequestRate">The request limit, or null when the site is not limited.</param>
public sealed record SiteOptionsValues(
    CanonicalHost CanonicalHost,
    int? ClientMaxBodySizeMib,
    int? StaticCacheDays,
    IReadOnlyList<SiteRedirectValue> Redirects,
    IReadOnlyList<SiteBasicAuthUserValue> BasicAuthUsers,
    IpAccessMode? IpAccessMode,
    IReadOnlyList<string> IpRules,
    SiteRequestRate? RequestRate);
