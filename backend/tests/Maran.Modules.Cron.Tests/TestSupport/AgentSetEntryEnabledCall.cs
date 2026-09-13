namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One request to switch a cron entry on or off.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="EntryId">The entry the call named.</param>
/// <param name="Enabled">The state the panel asked for.</param>
public sealed record AgentSetEntryEnabledCall(string AccountUsername, string EntryId, bool Enabled);
