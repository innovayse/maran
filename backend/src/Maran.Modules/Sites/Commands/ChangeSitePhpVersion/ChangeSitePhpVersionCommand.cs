using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.ChangeSitePhpVersion;

/// <summary>Rebinds a PHP-backed site to a different installed version and re-renders its pool.</summary>
/// <param name="SiteId">
/// The site to rebind. It comes from the route and is stamped by the action; a body that carries a
/// <c>siteId</c> is ignored, so a body id can never disagree with the path id.
/// </param>
/// <param name="PhpVersion">The installed version to switch to.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record ChangeSitePhpVersionCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid SiteId,
    string PhpVersion,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
