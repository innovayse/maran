namespace Maran.Modules.Sites.Commands.EnableSite;

/// <summary>
/// Restores normal serving for a disabled site. Idempotent in effect: enabling an enabled site leaves it
/// enabled.
/// </summary>
/// <param name="SiteId">The site to enable.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record EnableSiteCommand(Guid SiteId, string IpAddress, string UserAgent);
