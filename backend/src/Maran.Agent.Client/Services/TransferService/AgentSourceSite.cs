namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One site found on a source.</summary>
/// <param name="Domain">The primary host name, validated by the agent as a domain.</param>
/// <param name="Aliases">Other host names the virtual host answers to.</param>
/// <param name="Backend">What serves the site.</param>
/// <param name="PhpVersion">The PHP version as <c>major.minor</c> when <paramref name="Backend"/> is PHP, otherwise <c>null</c>.</param>
/// <param name="DocumentRoot">The document root on the source. Display only.</param>
public sealed record AgentSourceSite(
    string Domain,
    IReadOnlyList<string> Aliases,
    SourceSiteBackend Backend,
    string? PhpVersion,
    string DocumentRoot);
