namespace Maran.Modules.Accounts.Queries.GetMyAccount;

/// <summary>
/// Reads the account the CALLER owns. Carries no account id: the answer is always "the account the
/// caller's own token names", read off <see cref="SharedKernel.Interfaces.ICurrentUser.AccountId"/>
/// inside <see cref="GetMyAccountQueryHandler"/> — a parameter here would be a way to ask for
/// somebody else's account, and there is no such parameter to forge.
/// </summary>
public sealed record GetMyAccountQuery;
