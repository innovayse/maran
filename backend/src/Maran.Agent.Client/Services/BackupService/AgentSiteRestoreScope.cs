namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One site's directory, replaced by the backup's copy of it; no database is touched.</summary>
/// <param name="Domain">
/// The site's primary domain. The agent validates it as a domain and derives the directory the site
/// is served from itself, so this value can name no other path.
/// </param>
public sealed record AgentSiteRestoreScope(string Domain) : AgentRestoreScope;
