namespace Maran.Modules.Cron.Queries.GetCronEnvironment;

/// <summary>
/// Reads the environment assignments the agent manages in one account's crontab.
/// </summary>
/// <remarks>
/// Assignments the account or the host wrote outside the agent's own region are not reported, so
/// this describes the managed region and nothing else — the answer is what a write would replace,
/// which is the only reading a caller can safely edit and send back.
/// </remarks>
/// <param name="AccountId">The account whose crontab to read.</param>
public sealed record GetCronEnvironmentQuery(Guid AccountId);
