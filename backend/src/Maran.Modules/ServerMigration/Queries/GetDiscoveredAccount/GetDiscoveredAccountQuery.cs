namespace Maran.Modules.ServerMigration.Queries.GetDiscoveredAccount;

/// <summary>
/// Reads one account a discovery found, with its sites and databases. Built by the action from the route, never bound
/// from a request.
/// </summary>
/// <param name="ServerId">The server the account was found on.</param>
/// <param name="AccountId">The discovered account.</param>
public sealed record GetDiscoveredAccountQuery(Guid ServerId, Guid AccountId);
