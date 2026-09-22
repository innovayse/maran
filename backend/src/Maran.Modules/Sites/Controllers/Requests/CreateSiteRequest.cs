using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/sites</c>.</summary>
/// <remarks>
/// A separate type from the command: the command carries the caller's address and user agent,
/// which are read from the connection and must never be settable by the request that is being
/// audited.
/// </remarks>
/// <param name="AccountId">The account that will own the site.</param>
/// <param name="Domain">The primary domain the site serves.</param>
/// <param name="Aliases">Additional hostnames answered by the same vhost.</param>
/// <param name="BackendType">Which backend serves the site's content.</param>
/// <param name="PhpVersion">The installed PHP version to bind to; required when the backend is PHP.</param>
/// <param name="ProxyUpstream">The upstream to forward to; required when the backend is a reverse proxy.</param>
public sealed record CreateSiteRequest(
    Guid AccountId,
    string Domain,
    IReadOnlyList<string>? Aliases,
    SiteBackendType BackendType,
    string? PhpVersion,
    string? ProxyUpstream);
