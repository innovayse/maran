namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One agent call that names an account and one of its entries and nothing else.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="EntryId">The entry the call named.</param>
public sealed record AgentEntryCall(string AccountUsername, string EntryId);
