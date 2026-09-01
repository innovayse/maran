namespace Maran.Modules.Sites.Commands.DeleteSite;

/// <summary>
/// Removes a site: its vhost include is deleted and the web server reloaded. The customer's files
/// under the document root are deliberately left alone — deleting a site is not deleting data.
/// </summary>
/// <param name="SiteId">The site to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteSiteCommand(Guid SiteId, string IpAddress, string UserAgent);
