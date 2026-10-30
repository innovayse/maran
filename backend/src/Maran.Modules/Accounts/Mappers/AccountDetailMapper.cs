using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Translates an account and the plan it is on into <see cref="AccountDetailDto"/>. Shared by the
/// read of one account and by the two edits that answer with the account as it now stands, so the
/// page receives one shape whichever of them it called.
/// </summary>
public static class AccountDetailMapper
{
    /// <summary>Restates the account and its plan as the wire shape.</summary>
    /// <param name="account">The account.</param>
    /// <param name="plan">The plan <paramref name="account"/> is on.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    /// <returns>The account's detail view.</returns>
    public static AccountDetailDto ToDto(Account account, Plan plan, IStringLocalizer<DisplayNames> displayNames)
    {
        return new AccountDetailDto(
            account.Id,
            account.Name,
            account.PrimaryDomain,
            account.PlanId,
            account.Status,
            account.CreatedAt,
            PlanMapper.DisplayNameOf(plan, displayNames),
            account.OwnerEmail);
    }
}
