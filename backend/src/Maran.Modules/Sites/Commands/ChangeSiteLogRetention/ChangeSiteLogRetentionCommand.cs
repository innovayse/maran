using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.ChangeSiteLogRetention;

/// <summary>Sets how many days of rotated logs one site keeps (issue #152).</summary>
/// <param name="Days">The number of days, from 1 to 14. The installer's log rotation is the ceiling.</param>
/// <param name="SiteId">
/// The site to change. It comes from the route and is stamped by the action; a body that carries a
/// <c>siteId</c> is ignored, so a body id can never disagree with the path id.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record ChangeSiteLogRetentionCommand(
    int Days,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SiteId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
