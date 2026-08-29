namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Creates a hosting account row. This handler only creates the row: it does NOT provision the
/// account's Linux user, home directory, or quota — that happens through the agent's Accounts
/// operations, which do not exist yet (spec §8; out of scope for this pass).
/// </summary>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
public sealed record CreateAccountCommand(string Name, string PrimaryDomain, Guid PlanId);
