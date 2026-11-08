using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates an account as the list-shaped wire view, so the list and the operations that answer with
/// an account row (creating, suspending, reactivating, and a key's plan change) cannot come to disagree
/// about it.
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

    /// <summary>
    /// Narrows the single-resource view to the list view: the same account, without the plan's display
    /// name and without the owner's contact address. Used where a caller may act on an account but has
    /// no business holding the address its customer logs in with, such as an integration driven by an
    /// API key.
    /// </summary>
    /// <param name="detail">The account's single-resource view.</param>
    /// <returns>The account's list view, carrying every field the two views share.</returns>
    public static AccountDto ToDto(AccountDetailDto detail)
    {
        return new AccountDto(
            detail.Id,
            detail.Name,
            detail.PrimaryDomain,
            detail.PlanId,
            detail.Status,
            detail.StatusName,
            detail.CreatedAt,
            detail.ResellerId,
            detail.ResellerName);
    }

    /// <summary>
    /// Narrows an answer that carries the single-resource view to the same answer carrying the list view.
    /// A failure is passed on whole — its error and the extension that rides with it — so narrowing never
    /// changes what a refusal says.
    /// </summary>
    /// <param name="detail">The answer carrying the account's single-resource view.</param>
    /// <returns>The same answer carrying the account's list view.</returns>
    public static Result<AccountDto> ToDto(Result<AccountDetailDto> detail)
    {
        if (detail.IsSuccess)
        {
            return Result<AccountDto>.Ok(ToDto(detail.Value));
        }

        return detail.Extension is null
            ? Result<AccountDto>.Fail(detail.Error!)
            : Result<AccountDto>.Fail(detail.Error!, detail.Extension);
    }
}
