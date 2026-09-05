using Maran.Agent.Client.Services.CronService;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One request to install a new cron entry.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="Schedule">The schedule the panel sent.</param>
/// <param name="Command">The command the panel sent, verbatim.</param>
public sealed record AgentCreateEntryCall(string AccountUsername, AgentCronSchedule Schedule, string Command);
