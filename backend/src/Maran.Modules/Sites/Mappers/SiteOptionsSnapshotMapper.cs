using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Mappers;

/// <summary>
/// Restates a site's stored options as the <see cref="SiteOptionsSnapshot"/> another module reads
/// through the Sdk's site directory.
/// </summary>
/// <remarks>
/// Exists for the Ssl module: installing a certificate re-renders the vhost, and a snapshot without the
/// options would drop them on an action that had nothing to do with them.
/// </remarks>
public static class SiteOptionsSnapshotMapper
{
    /// <summary>Restates a site's options for another module.</summary>
    /// <param name="options">The stored options, or null when the site has none.</param>
    /// <param name="bodySizeMib">
    /// The request body limit the vhost carries, decided by <c>SiteBodySizePolicy</c>: the other module
    /// re-renders the vhost from this figure, so it has to be the one this module's own renders use.
    /// </param>
    /// <returns>The snapshot carrying those options.</returns>
    public static SiteOptionsSnapshot From(SiteOptions? options, int? bodySizeMib)
    {
        var values = SiteOptionsValuesMapper.From(options);

        return new SiteOptionsSnapshot(
            ToSdkCanonicalHost(values.CanonicalHost),
            bodySizeMib,
            values.StaticCacheDays,
            values.Redirects
                .Select(redirect =>
                {
                    return new SiteRedirectSnapshot(
                        redirect.Path,
                        ToSdkMatch(redirect.Match),
                        redirect.Target,
                        (int)redirect.Status);
                })
                .ToList(),
            values.BasicAuthUsers
                .Select(user =>
                {
                    return new SiteBasicAuthUserSnapshot(user.Name, user.PasswordHash);
                })
                .ToList(),
            values.IpAccessMode is { } mode
                ? new SiteIpAccessSnapshot(ToSdkMode(mode), values.IpRules)
                : null,
            values.RequestRate is { } rate ? new SiteRequestRateSnapshot(rate.PerSecond, rate.Burst) : null);
    }

    /// <summary>Maps the module's canonical-host enum onto the Sdk's.</summary>
    /// <param name="canonicalHost">The stored choice.</param>
    /// <returns>The Sdk's matching value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know: a bug, since both enums are closed.</exception>
    private static SiteCanonicalHost ToSdkCanonicalHost(CanonicalHost canonicalHost)
    {
        return canonicalHost switch
        {
            CanonicalHost.None => SiteCanonicalHost.None,
            CanonicalHost.Www => SiteCanonicalHost.Www,
            CanonicalHost.Apex => SiteCanonicalHost.Apex,
            _ => throw new ArgumentOutOfRangeException(nameof(canonicalHost), canonicalHost, "Unmapped canonical host."),
        };
    }

    /// <summary>Maps the module's redirect match enum onto the Sdk's.</summary>
    /// <param name="match">The stored comparison.</param>
    /// <returns>The Sdk's matching value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know: a bug, since both enums are closed.</exception>
    private static SiteRedirectMatch ToSdkMatch(RedirectMatch match)
    {
        return match switch
        {
            RedirectMatch.Exact => SiteRedirectMatch.Exact,
            RedirectMatch.Prefix => SiteRedirectMatch.Prefix,
            _ => throw new ArgumentOutOfRangeException(nameof(match), match, "Unmapped redirect match."),
        };
    }

    /// <summary>Maps the module's IP access mode onto the Sdk's.</summary>
    /// <param name="mode">The stored mode.</param>
    /// <returns>The Sdk's matching value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know: a bug, since both enums are closed.</exception>
    private static SiteIpAccessMode ToSdkMode(IpAccessMode mode)
    {
        return mode switch
        {
            IpAccessMode.AllowList => SiteIpAccessMode.AllowList,
            IpAccessMode.DenyList => SiteIpAccessMode.DenyList,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unmapped IP access mode."),
        };
    }
}
