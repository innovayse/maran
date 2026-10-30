namespace Maran.Modules.Accounts.Queries.GetMyAccountResourceSummary;

/// <summary>
/// Reads what the CALLER'S OWN account owns against its plan's limits. Carries no account id, for
/// the reason <c>GetMyAccountQuery</c> gives: the id comes from the caller's token, so there is no
/// parameter through which to ask for somebody else's account.
/// </summary>
public sealed record GetMyAccountResourceSummaryQuery;
