using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates an <see cref="AccountStatus"/> as the name a reader sees, in the request's culture. The
/// one place that name is looked up, so the word the status filter offers and the word a badge
/// prints cannot differ.
/// </summary>
/// <remarks>
/// Each name is looked up under <c>AccountStatus&lt;Member&gt;</c> in <c>DisplayNames</c>. A member
/// added to the enum without its three entries reads as its key, which
/// <c>ListAccountStatusChoicesQueryHandlerTests</c> catches by asking for every member.
/// </remarks>
public static class AccountStatusMapper
{
    /// <summary>The prefix an account state's resx key carries.</summary>
    private const string KeyPrefix = "AccountStatus";

    /// <summary>Names an account's state as a reader reads it.</summary>
    /// <param name="status">The state to name.</param>
    /// <param name="displayNames">Resolves the name in the request's culture.</param>
    /// <returns>The localized name.</returns>
    public static string NameOf(AccountStatus status, IStringLocalizer<DisplayNames> displayNames)
    {
        return displayNames[KeyPrefix + status].Value;
    }
}
