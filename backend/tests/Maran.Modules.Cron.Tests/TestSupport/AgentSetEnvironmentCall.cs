using Maran.Agent.Client.Services.CronService;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>One request to replace an account's managed environment assignments.</summary>
/// <param name="AccountUsername">The system user name the panel addressed the crontab by.</param>
/// <param name="Variables">The complete set the panel sent.</param>
public sealed record AgentSetEnvironmentCall(
    string AccountUsername,
    IReadOnlyList<AgentCronEnvVar> Variables);
