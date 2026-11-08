namespace Maran.Modules.Sites.Common;

/// <summary>The sums over a traffic window.</summary>
/// <param name="Requests">How many requests the window holds.</param>
/// <param name="Bytes">How many response body bytes they sent.</param>
public sealed record SiteTrafficTotalsDto(long Requests, long Bytes);
