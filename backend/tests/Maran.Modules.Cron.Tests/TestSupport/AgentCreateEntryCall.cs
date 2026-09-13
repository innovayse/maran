using Maran.Agent.Client.Services.CronService;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One request to install a new cron entry.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="Schedule">The schedule the panel sent.</param>
/// <param name="Command">The command the panel sent, verbatim.</param>
/// <param name="MaxEntries">
/// The plan allowance the panel stated, or <c>null</c> when it stated none. Recorded because the
/// allowance being ON the creation is the whole of this module's concurrency protection: a panel that
/// silently stopped sending it would leave the agent enforcing nothing, and no other observation of
/// this call would change.
/// </param>
public sealed record AgentCreateEntryCall(
    string AccountUsername,
    AgentCronSchedule Schedule,
    string Command,
    uint? MaxEntries);
