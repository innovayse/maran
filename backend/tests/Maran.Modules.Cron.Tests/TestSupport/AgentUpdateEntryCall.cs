using Maran.Agent.Client.Services.CronService;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One request to rewrite an existing cron entry.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="EntryId">The entry the call named.</param>
/// <param name="Schedule">The new schedule the panel sent.</param>
/// <param name="Command">The new command the panel sent, verbatim.</param>
public sealed record AgentUpdateEntryCall(
    string AccountUsername,
    string EntryId,
    AgentCronSchedule Schedule,
    string Command);
