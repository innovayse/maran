using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Mappers;

/// <summary>Restates a stored <see cref="SiteOptions"/> as the <see cref="SiteOptionsValues"/> a replacement is stated in.</summary>
/// <remarks>
/// It exists so the "before" of a change and the "after" of a request are the same shape and can be
/// compared, and so a site that has no options row reads as the empty set instead of as a special case
/// in every caller.
/// </remarks>
public static class SiteOptionsValuesMapper
{
    /// <summary>Restates a site's options, or the empty set for a site that has none.</summary>
    /// <param name="options">The stored options, or null when the site has none.</param>
    /// <returns>The values the options hold.</returns>
    public static SiteOptionsValues From(SiteOptions? options)
    {
        if (options is null)
        {
            return new SiteOptionsValues(CanonicalHost.None, null, null, [], [], null, [], null);
        }

        return new SiteOptionsValues(
            options.CanonicalHost,
            options.ClientMaxBodySizeMib,
            options.StaticCacheDays,
            options.Redirects
                .OrderBy(redirect =>
                {
                    return redirect.Position;
                })
                .Select(redirect =>
                {
                    return new SiteRedirectValue(redirect.Path, redirect.Match, redirect.Target, redirect.Status);
                })
                .ToList(),
            options.BasicAuthUsers
                .OrderBy(user =>
                {
                    return user.Position;
                })
                .Select(user =>
                {
                    return new SiteBasicAuthUserValue(user.Name, user.PasswordHash);
                })
                .ToList(),
            options.IpAccessMode,
            options.IpRules
                .OrderBy(rule =>
                {
                    return rule.Position;
                })
                .Select(rule =>
                {
                    return rule.Cidr;
                })
                .ToList(),
            options.RequestRate);
    }
}
