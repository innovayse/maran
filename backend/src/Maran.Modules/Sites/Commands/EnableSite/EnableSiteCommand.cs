namespace Maran.Modules.Sites.Commands.EnableSite;

/// <summary>Restores normal serving for a disabled site. Idempotent: enabling an enabled site changes nothing.</summary>
/// <param name="SiteId">The site to enable.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record EnableSiteCommand(Guid SiteId, string IpAddress, string UserAgent);
