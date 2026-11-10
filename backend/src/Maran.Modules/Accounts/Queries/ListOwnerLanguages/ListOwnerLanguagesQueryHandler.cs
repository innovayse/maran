using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Constants;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListOwnerLanguages;

/// <summary>
/// Handles <see cref="ListOwnerLanguagesQuery"/>: <see cref="PanelLanguages.Supported"/> in its own
/// order, each with its name.
/// </summary>
/// <remarks>
/// Each name is looked up by <see cref="OwnerLanguageMapper"/> under <c>OwnerLanguage&lt;CODE&gt;</c> in <c>DisplayNames</c>. A language is
/// named in itself in every culture's table, so the picker reads the same in whichever language the
/// administrator uses. A language added to <see cref="PanelLanguages.Supported"/> without its three
/// entries reads as its key, which the handler's test catches by asking for every language.
/// </remarks>
public sealed class ListOwnerLanguagesQueryHandler
{
    /// <summary>Resolves a language's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">Resolves a language's name in the request's culture.</param>
    public ListOwnerLanguagesQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every language an owner's invitation can be written in.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <returns>The languages, each with its name.</returns>
    public Task<Result<IReadOnlyList<OwnerLanguageDto>>> HandleAsync(ListOwnerLanguagesQuery query)
    {
        IReadOnlyList<OwnerLanguageDto> languages = PanelLanguages.Supported
            .Select(language =>
            {
                return OwnerLanguageMapper.ToDto(language, _displayNames);
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<OwnerLanguageDto>>.Ok(languages));
    }
}
