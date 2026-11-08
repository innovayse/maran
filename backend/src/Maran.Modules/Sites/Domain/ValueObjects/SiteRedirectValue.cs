using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>One redirect rule as a caller states it, before it becomes a stored row.</summary>
/// <param name="Path">The path the rule applies to; starts with "/", and ends in "/" for a prefix rule.</param>
/// <param name="Match">How <paramref name="Path"/> is compared with the request path.</param>
/// <param name="Target">Where requests are sent: an absolute http(s) URL or a path on the same site.</param>
/// <param name="Status">The HTTP status the redirect answers with.</param>
public sealed record SiteRedirectValue(string Path, RedirectMatch Match, string Target, RedirectStatus Status);
