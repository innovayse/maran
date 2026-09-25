using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// A customer's own view of the account they own: identity, status, and the plan's limits. Answers
/// "the account you own" for whoever the caller's own token names — never a wider or narrower view
/// (see <see cref="Queries.GetMyAccount.GetMyAccountQueryHandler"/>).
/// </summary>
/// <remarks>
/// Deliberately omits <see cref="Account.OwnerEmail"/>. It is the administrator's own contact
/// record for the account rather than something the account's login controls, it can diverge from
/// the login's own address (see <see cref="Account"/>'s remarks), and this endpoint's contract
/// (task 8) names exactly the fields below. Surfacing it here would let a customer's own screen show
/// an address they never set and cannot change, with no indication of either.
/// </remarks>
/// <param name="Id">The account's identity.</param>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="Status">The account's current lifecycle state.</param>
/// <param name="Plan">The limits of the plan this account is created against.</param>
public sealed record MyAccountDto(
    Guid Id,
    string Name,
    string PrimaryDomain,
    AccountStatus Status,
    MyAccountPlanDto Plan);
