namespace Maran.Modules.Accounts.Queries.GetAccount;

/// <summary>Reads one hosting account.</summary>
/// <param name="AccountId">The account to read.</param>
public sealed record GetAccountQuery(Guid AccountId);
