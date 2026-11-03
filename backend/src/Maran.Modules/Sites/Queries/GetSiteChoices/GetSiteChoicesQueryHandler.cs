using System.Text.Json;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Sites.Queries.GetSiteChoices;

/// <summary>Handles <see cref="GetSiteChoicesQuery"/> from the module's own enums and resource tables.</summary>
/// <remarks>
/// Server-wide reference data, not tenant data: the sets are the same for every account, so nothing
/// is read from the database and there is nothing to scope. Each list is built from the enum the
/// validators and the entity use, so the options the SPA offers and the values the server accepts
/// cannot drift apart. A value's wire spelling is the camel-cased member name, the one the API
/// serializes the enum with; its label is the resource entry <c>SiteBackendType&lt;Member&gt;</c> or
/// <c>SiteStatus&lt;Member&gt;</c> in the caller's language.
/// </remarks>
public sealed class GetSiteChoicesQueryHandler
{
    /// <summary>The module's display-name table, in the caller's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">The module's display-name table, in the caller's culture.</param>
    public GetSiteChoicesQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns the backend types and serving states, labelled in the caller's language.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Unused: nothing is read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<SiteChoicesDto>> HandleAsync(GetSiteChoicesQuery query, CancellationToken cancellationToken)
    {
        var choices = new SiteChoicesDto(
            ChoicesOf<SiteBackendType>("SiteBackendType"),
            ChoicesOf<SiteStatus>("SiteStatus"));

        return Task.FromResult(Result<SiteChoicesDto>.Ok(choices));
    }

    /// <summary>Lists every member of an enum as a choice, in declaration order.</summary>
    /// <typeparam name="TEnum">The closed set to list.</typeparam>
    /// <param name="keyPrefix">The resource key prefix; the member's name completes the key.</param>
    /// <returns>One choice per member.</returns>
    private List<SiteChoiceDto> ChoicesOf<TEnum>(string keyPrefix)
        where TEnum : struct, Enum
    {
        return Enum.GetNames<TEnum>().Select(name =>
        {
            return new SiteChoiceDto(JsonNamingPolicy.CamelCase.ConvertName(name), _displayNames[keyPrefix + name]);
        }).ToList();
    }
}
