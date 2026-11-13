using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Mappers;

/// <summary>
/// Builds the <see cref="SiteOptionsDescriptor"/> the agent renders into a vhost, from a site's
/// stored options.
/// </summary>
/// <remarks>
/// Called by <see cref="SiteDescriptorMapper"/> and by nothing else, so the options travel with the
/// site on every re-render. The agent's statuses are the HTTP codes themselves, which is why the
/// redirect status is a cast.
/// </remarks>
public static class SiteOptionsDescriptorMapper
{
    /// <summary>Describes a site's options to the agent.</summary>
    /// <param name="options">The stored options, or null when the site has none.</param>
    /// <param name="bodySizeMib">
    /// The request body limit the vhost carries, decided by <see cref="SiteBodySizePolicy"/> and not read
    /// from the options: a stored figure is the owner's wish, and the limit the host serves is that wish
    /// under the plan, or the plan's bound when there is none.
    /// </param>
    /// <returns>The descriptor carrying those options; <see cref="SiteOptionsDescriptor.None"/>'s shape when there are none.</returns>
    public static SiteOptionsDescriptor From(SiteOptions? options, int? bodySizeMib)
    {
        var values = SiteOptionsValuesMapper.From(options);

        return new SiteOptionsDescriptor(
            ToCanonicalHostKind(values.CanonicalHost),
            bodySizeMib is { } bodySize ? checked((uint)bodySize) : null,
            values.StaticCacheDays is { } cacheDays ? checked((uint)cacheDays) : null,
            values.Redirects
                .Select(redirect =>
                {
                    return new RedirectRuleDescriptor(
                        redirect.Path,
                        ToRedirectMatchKind(redirect.Match),
                        redirect.Target,
                        (uint)redirect.Status);
                })
                .ToList(),
            values.BasicAuthUsers
                .Select(user =>
                {
                    return new BasicAuthUserDescriptor(user.Name, user.PasswordHash);
                })
                .ToList(),
            ToIpAccess(values),
            values.RequestRate is { } rate
                ? new RequestRateDescriptor(checked((uint)rate.PerSecond), checked((uint)rate.Burst))
                : null);
    }

    /// <summary>Describes the IP rules, or none when the site has no rules.</summary>
    /// <param name="values">The options' values.</param>
    /// <returns>The IP access descriptor, or null.</returns>
    private static IpAccessDescriptor? ToIpAccess(SiteOptionsValues values)
    {
        if (values.IpAccessMode is not { } mode)
        {
            return null;
        }

        return new IpAccessDescriptor(
            mode switch
            {
                IpAccessMode.AllowList => IpAccessKind.AllowList,
                IpAccessMode.DenyList => IpAccessKind.DenyList,
                _ => throw new ArgumentOutOfRangeException(nameof(values), mode, "Unmapped IP access mode."),
            },
            values.IpRules);
    }

    /// <summary>Maps the module's canonical-host enum onto the agent client's.</summary>
    /// <param name="canonicalHost">The stored choice.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a value the mapping does not know. A bug, not a domain failure: the enum is closed and
    /// the database stores its names. Failing loudly beats defaulting to "none", which would silently
    /// drop a site's canonical redirect.
    /// </exception>
    private static CanonicalHostKind ToCanonicalHostKind(CanonicalHost canonicalHost)
    {
        return canonicalHost switch
        {
            CanonicalHost.None => CanonicalHostKind.None,
            CanonicalHost.Www => CanonicalHostKind.Www,
            CanonicalHost.Apex => CanonicalHostKind.Apex,
            _ => throw new ArgumentOutOfRangeException(nameof(canonicalHost), canonicalHost, "Unmapped canonical host."),
        };
    }

    /// <summary>Maps the module's redirect match enum onto the agent client's.</summary>
    /// <param name="match">The stored comparison.</param>
    /// <returns>The agent client's matching kind.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know; see <see cref="ToCanonicalHostKind"/>.</exception>
    private static RedirectMatchKind ToRedirectMatchKind(RedirectMatch match)
    {
        return match switch
        {
            RedirectMatch.Exact => RedirectMatchKind.Exact,
            RedirectMatch.Prefix => RedirectMatchKind.Prefix,
            _ => throw new ArgumentOutOfRangeException(nameof(match), match, "Unmapped redirect match."),
        };
    }
}
