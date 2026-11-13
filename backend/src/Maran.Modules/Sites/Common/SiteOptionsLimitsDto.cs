namespace Maran.Modules.Sites.Common;

/// <summary>
/// The bounds the options screen must respect, read from the server rather than written into the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <param name="MaximumRedirects">The most redirect rules one site may carry.</param>
/// <param name="MaximumBasicAuthUsers">The most basic-authentication users one site may carry.</param>
/// <param name="MaximumIpRules">The most IP rules one site may carry.</param>
/// <param name="MinimumBodySizeMib">The smallest request body limit that may be set, in MiB.</param>
/// <param name="MaximumBodySizeMib">The largest request body limit that may be set, in MiB.</param>
/// <param name="PlanMaximumBodySizeMib">
/// The largest request body limit the site's plan allows, in MiB: the plan's upload bound kept inside
/// the range above. Null for a site the plan's PHP bounds do not govern. The screen shows it and checks
/// against it as advice; the server refuses a larger figure with <c>SiteBodySizeAbovePlan</c>.
/// </param>
/// <param name="MinimumCacheDays">The smallest static-file cache lifetime that may be set, in days.</param>
/// <param name="MaximumCacheDays">The largest static-file cache lifetime that may be set, in days.</param>
/// <param name="MinimumPasswordLength">The shortest basic-authentication password, in characters.</param>
/// <param name="MinimumRequestsPerSecond">The smallest request rate that may be set, in requests per second per visitor address.</param>
/// <param name="MaximumRequestsPerSecond">The largest request rate that may be set, in requests per second per visitor address.</param>
/// <param name="MinimumRequestBurst">The smallest request burst that may be set.</param>
/// <param name="MaximumRequestBurst">The largest request burst that may be set.</param>
public sealed record SiteOptionsLimitsDto(
    int MaximumRedirects,
    int MaximumBasicAuthUsers,
    int MaximumIpRules,
    int MinimumBodySizeMib,
    int MaximumBodySizeMib,
    int? PlanMaximumBodySizeMib,
    int MinimumCacheDays,
    int MaximumCacheDays,
    int MinimumPasswordLength,
    int MinimumRequestsPerSecond,
    int MaximumRequestsPerSecond,
    int MinimumRequestBurst,
    int MaximumRequestBurst);
