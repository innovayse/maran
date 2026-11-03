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
    /// <param name="resellerName">The owning reseller's name, or <c>null</c> for the administrator's own account.</param>
    /// <returns>The account's detail view.</returns>
    public static AccountDetailDto ToDto(
        Account account,
        Plan plan,
        IStringLocalizer<DisplayNames> displayNames,
        string? resellerName)
    {
        return new AccountDetailDto(
            account.Id,
            account.Name,
            account.PrimaryDomain,
            account.PlanId,
            account.Status,
            AccountStatusMapper.NameOf(account.Status, displayNames),
            account.CreatedAt,
            PlanMapper.DisplayNameOf(plan, displayNames),
            account.OwnerEmail,
            account.ResellerId,
            resellerName);
    }
}
