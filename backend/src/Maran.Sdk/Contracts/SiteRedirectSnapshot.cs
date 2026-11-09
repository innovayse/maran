namespace Maran.Sdk.Contracts;

/// <summary>One redirect rule of a site, as one module reports it to another.</summary>
/// <param name="Path">The path the rule applies to.</param>
/// <param name="Match">How <paramref name="Path"/> is compared with the request path.</param>
/// <param name="Target">Where requests are sent: an absolute http(s) URL or a path on the same site.</param>
/// <param name="StatusCode">The HTTP status of the redirect: 301 or 302.</param>
public sealed record SiteRedirectSnapshot(string Path, SiteRedirectMatch Match, string Target, int StatusCode);
