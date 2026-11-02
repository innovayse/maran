namespace Maran.Modules.Identity.Queries.ListMyAccounts;

/// <summary>Lists the hosting accounts the signed-in person holds a membership of.</summary>
/// <param name="UserId">
/// Whose accounts to list. Taken from the caller's own token by the controller and never from the
/// request, so there is no parameter with which to ask for somebody else's.
/// </param>
/// <param name="ActiveAccountId">
/// The account the caller's token currently speaks for, taken from its account claim by the
/// controller; null when it speaks for none.
/// </param>
public sealed record ListMyAccountsQuery(Guid UserId, Guid? ActiveAccountId);
