namespace Maran.Modules.Sites.Commands.ChangeSitePhpVersion;

/// <summary>Rebinds a PHP-backed site to a different installed version and re-renders its pool.</summary>
/// <param name="SiteId">The site to rebind.</param>
/// <param name="PhpVersion">The installed version to switch to.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record ChangeSitePhpVersionCommand(Guid SiteId, string PhpVersion, string IpAddress, string UserAgent);
