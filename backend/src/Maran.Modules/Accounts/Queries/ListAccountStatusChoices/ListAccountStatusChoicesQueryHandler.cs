using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListAccountStatusChoices;

/// <summary>
/// Handles <see cref="ListAccountStatusChoicesQuery"/> by naming every <see cref="AccountStatus"/>
/// member for the request's culture.
/// </summary>
/// <remarks>
/// The names come from <see cref="AccountStatusMapper"/>, the same lookup the account views use for
/// their <c>StatusName</c>. A member added to the enum without its three entries reads as its key,
/// which the handler's test catches by asking for every member.
/// </remarks>
public sealed class ListAccountStatusChoicesQueryHandler
{
    /// <summary>Resolves a state's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">Resolves a state's name in the request's culture.</param>
    public ListAccountStatusChoicesQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every account state, named, in declaration order.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<AccountStatusChoiceDto>>> HandleAsync(ListAccountStatusChoicesQuery query)
    {
        IReadOnlyList<AccountStatusChoiceDto> choices = Enum.GetValues<AccountStatus>()
            .Select(status =>
            {
                return new AccountStatusChoiceDto(status, AccountStatusMapper.NameOf(status, _displayNames));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<AccountStatusChoiceDto>>.Ok(choices));
    }
}
