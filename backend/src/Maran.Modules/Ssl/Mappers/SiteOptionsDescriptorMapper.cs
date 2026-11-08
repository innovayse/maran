using Maran.Agent.Client.Services.SitesService;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>
/// Builds the <see cref="SiteOptionsDescriptor"/> the agent renders into a vhost, from the
/// <see cref="SiteOptionsSnapshot"/> the Sites module handed over.
/// </summary>
/// <remarks>
/// Installing or removing a certificate re-renders the whole vhost, so the site's redirects, basic
/// authentication, IP rules and request limit have to be told to the agent again: a descriptor without them would
/// silently strip a site of its access control and request limit on an action that had nothing to do with it. Used by
/// <see cref="SiteDescriptorMapper"/> and by nothing else. The same name exists in the Sites module for
/// the same product and a different source (rules/csharp.md "Two modules may hold a same-named file").
/// </remarks>
public static class SiteOptionsDescriptorMapper
{
    /// <summary>Describes a site's options to the agent.</summary>
    /// <param name="options">The options the owning module reported.</param>
    /// <returns>The descriptor carrying exactly those options.</returns>
    public static SiteOptionsDescriptor From(SiteOptionsSnapshot options)
    {
        return new SiteOptionsDescriptor(
            ToCanonicalHostKind(options.CanonicalHost),
            options.ClientMaxBodySizeMib is { } bodySize ? checked((uint)bodySize) : null,
            options.StaticCacheDays is { } cacheDays ? checked((uint)cacheDays) : null,
            options.Redirects
                .Select(redirect =>
                {
                    return new RedirectRuleDescriptor(
                        redirect.Path,
                        ToRedirectMatchKind(redirect.Match),
                        redirect.Target,
                        checked((uint)redirect.StatusCode));
                })
                .ToList(),
            options.BasicAuthUsers
                .Select(user =>
                {
                    return new BasicAuthUserDescriptor(user.Name, user.PasswordHash);
                })
                .ToList(),
            options.IpAccess is null
                ? null
                : new IpAccessDescriptor(ToIpAccessKind(options.IpAccess.Mode), options.IpAccess.Cidrs),
            options.RequestRate is { } rate
                ? new RequestRateDescriptor(checked((uint)rate.PerSecond), checked((uint)rate.Burst))
                : null);
    }

    /// <summary>Maps the Sdk's canonical-host value onto the agent client's.</summary>
    /// <param name="canonicalHost">The choice the owning module reported.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a value the mapping does not know: a bug, since both enums are closed. Failing loudly
    /// beats defaulting to "none", which would silently drop a site's canonical redirect.
    /// </exception>
    private static CanonicalHostKind ToCanonicalHostKind(SiteCanonicalHost canonicalHost)
    {
        return canonicalHost switch
        {
            SiteCanonicalHost.None => CanonicalHostKind.None,
            SiteCanonicalHost.Www => CanonicalHostKind.Www,
            SiteCanonicalHost.Apex => CanonicalHostKind.Apex,
            _ => throw new ArgumentOutOfRangeException(nameof(canonicalHost), canonicalHost, "Unmapped canonical host."),
        };
    }

    /// <summary>Maps the Sdk's redirect match value onto the agent client's.</summary>
    /// <param name="match">The comparison the owning module reported.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know; see <see cref="ToCanonicalHostKind"/>.</exception>
    private static RedirectMatchKind ToRedirectMatchKind(SiteRedirectMatch match)
    {
        return match switch
        {
            SiteRedirectMatch.Exact => RedirectMatchKind.Exact,
            SiteRedirectMatch.Prefix => RedirectMatchKind.Prefix,
            _ => throw new ArgumentOutOfRangeException(nameof(match), match, "Unmapped redirect match."),
        };
    }

    /// <summary>Maps the Sdk's IP access mode onto the agent client's.</summary>
    /// <param name="mode">The mode the owning module reported.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know; see <see cref="ToCanonicalHostKind"/>.</exception>
    private static IpAccessKind ToIpAccessKind(SiteIpAccessMode mode)
    {
        return mode switch
        {
            SiteIpAccessMode.AllowList => IpAccessKind.AllowList,
            SiteIpAccessMode.DenyList => IpAccessKind.DenyList,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unmapped IP access mode."),
        };
    }
}
