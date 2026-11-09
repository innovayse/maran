using System.Text.Json.Serialization;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.ReplaceSiteOptions;

/// <summary>
/// Replaces ALL of a site's per-site options with the set it states: anything not in it is removed. A
/// replacement and not a patch, so a repeated request converges to the same state and the screen never
/// has to say which fields it left alone.
/// </summary>
/// <param name="SiteId">
/// The site whose options are replaced. It comes from the route and is stamped by the action; a body
/// that carries a <c>siteId</c> is ignored, so a body id can never disagree with the path id.
/// </param>
/// <param name="CanonicalHost">Which of the apex / "www." pair is canonical; <c>none</c> for no redirect.</param>
/// <param name="ClientMaxBodySizeMib">The largest request body in MiB, or null for the web server's default.</param>
/// <param name="StaticCacheDays">How many days browsers may cache static files, or null for no cache headers.</param>
/// <param name="Redirects">The redirect rules, in the order they are rendered; empty for none.</param>
/// <param name="BasicAuthUsers">
/// The users allowed in; empty for no authentication. A user already on the site may omit the password
/// to keep it; a user not yet on the site must state one.
/// </param>
/// <param name="IpAccessMode">Whether <paramref name="IpRules"/> allows or refuses; null exactly when there are no rules.</param>
/// <param name="IpRules">The addresses or CIDR ranges the access mode applies to; empty for none.</param>
/// <param name="RequestRate">
/// How fast one visitor address may request the site, or null for no limit. A site that is not limited
/// states null and never a rate of zero.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
public sealed record ReplaceSiteOptionsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SiteId,
    CanonicalHost CanonicalHost,
    int? ClientMaxBodySizeMib,
    int? StaticCacheDays,
    IReadOnlyList<SiteRedirectDto> Redirects,
    IReadOnlyList<SiteBasicAuthUserInputDto> BasicAuthUsers,
    IpAccessMode? IpAccessMode,
    IReadOnlyList<string> IpRules,
    SiteRequestRateDto? RequestRate,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
