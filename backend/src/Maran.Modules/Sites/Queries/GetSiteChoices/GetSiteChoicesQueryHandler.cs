using System.Globalization;
using System.Text.Json;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.GetSiteChoices;

/// <summary>Handles <see cref="GetSiteChoicesQuery"/> from the module's own enums, policies and resource tables.</summary>
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
    /// <summary>Names the backends and serving states in the caller's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="siteNames">Names the backends and serving states in the caller's culture.</param>
    public GetSiteChoicesQueryHandler(SiteDisplayNames siteNames)
    {
        _siteNames = siteNames;
    }

    /// <summary>
    /// Returns the backend types, serving states, traffic windows and log retentions, labelled in the
    /// caller's language. The windows and retentions come from the policies the validators use.
    /// </summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Unused: nothing is read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<SiteChoicesDto>> HandleAsync(GetSiteChoicesQuery query, CancellationToken cancellationToken)
    {
        var choices = new SiteChoicesDto(
            ChoicesOf<SiteBackendType>(_siteNames.Of),
            ChoicesOf<SiteStatus>(_siteNames.Of),
            TrafficHistory.Windows.Select(days =>
            {
                return new SiteChoiceDto(days.ToString(CultureInfo.InvariantCulture), _siteNames.OfTrafficWindow(days));
            }).ToList(),
            Enumerable.Range(SiteLogRetention.MinimumDays, SiteLogRetention.MaximumDays - SiteLogRetention.MinimumDays + 1)
                .Select(days =>
                {
                    return new SiteChoiceDto(days.ToString(CultureInfo.InvariantCulture), _siteNames.OfLogRetention(days));
                })
                .ToList());

        return Task.FromResult(Result<SiteChoicesDto>.Ok(choices));
    }

    /// <summary>Lists every member of an enum as a choice, in declaration order.</summary>
    /// <typeparam name="TEnum">The closed set to list.</typeparam>
    /// <param name="nameOf">Names one member in the caller's culture.</param>
    /// <returns>One choice per member.</returns>
    private static List<SiteChoiceDto> ChoicesOf<TEnum>(Func<TEnum, string> nameOf)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Select(member =>
        {
            return new SiteChoiceDto(JsonNamingPolicy.CamelCase.ConvertName(member.ToString()), nameOf(member));
        }).ToList();
    }
}
