using System.Text.Json;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Mappers;

/// <summary>Restates a site's options as the outward <see cref="SiteOptionsDto"/>, with its bounds and choices.</summary>
/// <remarks>
/// The bounds and the choices travel with every answer so the SPA holds neither: the screen offers
/// what the server accepts and shows the server's own limits (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). Basic-authentication users are restated by name only; the hash is
/// not a field of any outward type.
/// </remarks>
public static class SiteOptionsMapper
{
    /// <summary>Restates a site's options for the options screen.</summary>
    /// <param name="site">The site whose options are shown; <see cref="Site.Options"/> may be null.</param>
    /// <param name="names">Labels the closed sets in the request's culture.</param>
    /// <param name="limits">The plan's PHP bounds of the site's account, which cap its body size.</param>
    /// <returns>The outward view.</returns>
    public static SiteOptionsDto From(Site site, SiteDisplayNames names, PhpLimitsSnapshot limits)
    {
        var values = SiteOptionsValuesMapper.From(site.Options);

        return new SiteOptionsDto(
            site.Id,
            values.CanonicalHost,
            values.ClientMaxBodySizeMib,
            values.StaticCacheDays,
            values.Redirects
                .Select(redirect =>
                {
                    return new SiteRedirectDto(redirect.Path, redirect.Match, redirect.Target, redirect.Status);
                })
                .ToList(),
            values.BasicAuthUsers
                .Select(user =>
                {
                    return user.Name;
                })
                .ToList(),
            values.IpAccessMode,
            values.IpRules,
            values.RequestRate is { } rate ? new SiteRequestRateDto(rate.PerSecond, rate.Burst) : null,
            new SiteOptionsLimitsDto(
                SiteOptionsLimits.MaximumRedirects,
                SiteOptionsLimits.MaximumBasicAuthUsers,
                SiteOptionsLimits.MaximumIpRules,
                SiteOptionsLimits.MinimumBodySizeMib,
                SiteOptionsLimits.MaximumBodySizeMib,
                SiteBodySizePolicy.PlanCeiling(site.BackendType, limits),
                SiteOptionsLimits.MinimumCacheDays,
                SiteOptionsLimits.MaximumCacheDays,
                SiteOptionsLimits.MinimumPasswordLength,
                SiteOptionsLimits.MinimumRequestsPerSecond,
                SiteOptionsLimits.MaximumRequestsPerSecond,
                SiteOptionsLimits.MinimumRequestBurst,
                SiteOptionsLimits.MaximumRequestBurst),
            new SiteOptionsChoicesDto(
                ChoicesOf<CanonicalHost>(names.Of),
                ChoicesOf<RedirectMatch>(names.Of),
                ChoicesOf<RedirectStatus>(names.Of),
                ChoicesOf<IpAccessMode>(names.Of)));
    }

    /// <summary>Lists every member of a closed set with its localized label, valued as the wire spells it.</summary>
    /// <typeparam name="TEnum">The closed set.</typeparam>
    /// <param name="nameOf">Labels one member.</param>
    /// <returns>One choice per member, in declaration order.</returns>
    private static List<SiteChoiceDto> ChoicesOf<TEnum>(Func<TEnum, string> nameOf)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Select(member =>
        {
            return new SiteChoiceDto(JsonNamingPolicy.CamelCase.ConvertName(member.ToString()), nameOf(member));
        }).ToList();
    }
}
