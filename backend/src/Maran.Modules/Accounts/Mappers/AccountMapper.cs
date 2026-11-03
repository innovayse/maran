using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates an account as the list-shaped wire view, so the list and the three operations that answer
/// with an account row (creating, suspending, reactivating) cannot come to disagree about it.
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): the
/// owner's name arrives already looked up, and the state's name is the one
/// <see cref="AccountStatusMapper"/> gives it.
/// </remarks>
public static class AccountMapper
{
    /// <summary>Restates the account as its list view.</summary>
    /// <param name="account">The account.</param>
    /// <param name="displayNames">Names the account's state in the request's culture.</param>
    /// <param name="resellerName">The owning reseller's name, or <c>null</c> for the administrator's own account.</param>
    /// <returns>The account's list view.</returns>
    public static AccountDto ToDto(Account account, IStringLocalizer<DisplayNames> displayNames, string? resellerName)
    {
        return new AccountDto(
            account.Id,
            account.Name,
            account.PrimaryDomain,
            account.PlanId,
            account.Status,
            AccountStatusMapper.NameOf(account.Status, displayNames),
            account.CreatedAt,
            account.ResellerId,
            resellerName);
    }
}
