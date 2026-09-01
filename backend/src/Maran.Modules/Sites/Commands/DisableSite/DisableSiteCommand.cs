namespace Maran.Modules.Sites.Commands.DisableSite;

/// <summary>
/// Serves a suspension response in place of the site's content, keeping its vhost, aliases and log
/// paths in place. Idempotent: disabling a disabled site changes nothing.
/// </summary>
/// <param name="SiteId">The site to disable.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DisableSiteCommand(Guid SiteId, string IpAddress, string UserAgent);
