namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One database, dropped and reloaded from the backup's dump of it; no file is touched.</summary>
/// <param name="DatabaseName">
/// The database's full, account-prefixed name, as the panel's own rows spell it. The agent accepts it
/// only when it also decodes to the account being restored and is among the databases the panel
/// still knows.
/// </param>
public sealed record AgentDatabaseRestoreScope(string DatabaseName) : AgentRestoreScope;
