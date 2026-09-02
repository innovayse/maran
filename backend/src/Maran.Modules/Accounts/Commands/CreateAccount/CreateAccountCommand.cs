namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Creates a hosting account: its Linux user, home directory and disk quota through the agent,
/// and then the row that records it (spec §8 — an Account IS a system user, and the isolation
/// between customers is the operating system's).
/// </summary>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record CreateAccountCommand(
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    string IpAddress,
    string UserAgent);
