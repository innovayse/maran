namespace Maran.Modules.Accounts.Queries.GetAccountResourceSummary;

/// <summary>Reads what one hosting account owns against its plan's limits (administrators).</summary>
/// <param name="AccountId">The account to summarise.</param>
public sealed record GetAccountResourceSummaryQuery(Guid AccountId);
