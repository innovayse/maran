using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>
/// The per-site options the agent renders into a vhost: canonical host, upload size, cache headers,
/// redirects, basic authentication, IP rules and request limit.
/// </summary>
/// <remarks>
/// Part of <see cref="SiteDescriptor"/>, so every re-render of a vhost carries them from the stored row.
/// There is no default argument anywhere on purpose: a call site that forgot the options would silently
/// render a site without its redirects, credentials and IP rules, so it must say <see cref="None"/>.
/// Absent is expressed by absence — a null body size, a zero day count, empty lists, no IP rules — never
/// by a sentinel mixed into a value.
/// </remarks>
/// <param name="CanonicalHost">Which of the apex / "www." pair is canonical.</param>
/// <param name="ClientMaxBodySizeMib">The largest request body in MiB, or null to leave the web server's default.</param>
/// <param name="StaticCacheDays">How many days browsers may cache static files, or null to send no cache headers.</param>
/// <param name="Redirects">The redirect rules, in the order they are rendered.</param>
/// <param name="BasicAuthUsers">The basic-authentication users; empty means no authentication.</param>
/// <param name="IpAccess">The IP rules, or null when the site has none.</param>
/// <param name="RequestRate">The request limit, or null when the site is not limited.</param>
public sealed record SiteOptionsDescriptor(
    CanonicalHostKind CanonicalHost,
    uint? ClientMaxBodySizeMib,
    uint? StaticCacheDays,
    IReadOnlyList<RedirectRuleDescriptor> Redirects,
    IReadOnlyList<BasicAuthUserDescriptor> BasicAuthUsers,
    IpAccessDescriptor? IpAccess,
    RequestRateDescriptor? RequestRate)
{
    /// <summary>The options of a site that has none set.</summary>
    public static readonly SiteOptionsDescriptor None = new(CanonicalHostKind.None, null, null, [], [], null, null);

    /// <summary>Converts these options into their wire message.</summary>
    /// <returns>The populated <see cref="SiteOptions"/>; its zero values mean "absent".</returns>
    internal SiteOptions ToWire()
    {
        var wire = new SiteOptions
        {
            CanonicalHost = CanonicalHost switch
            {
                CanonicalHostKind.None => Maran.Agent.V1.CanonicalHost.Unspecified,
                CanonicalHostKind.Www => Maran.Agent.V1.CanonicalHost.Www,
                CanonicalHostKind.Apex => Maran.Agent.V1.CanonicalHost.Apex,
                _ => throw new ArgumentOutOfRangeException(nameof(CanonicalHost), CanonicalHost, "Unknown canonical host kind."),
            },
            ClientMaxBodySizeMib = ClientMaxBodySizeMib ?? 0,
            StaticCacheDays = StaticCacheDays ?? 0,
        };
        wire.Redirects.AddRange(Redirects.Select(rule =>
        {
            return rule.ToWire();
        }));

        // The message itself is absent when there are no users: proto3 cannot tell an empty BasicAuth
        // from none, and the agent reads a present-but-empty message as a malformed request.
        if (BasicAuthUsers.Count > 0)
        {
            wire.BasicAuth = new BasicAuth();
            wire.BasicAuth.Users.AddRange(BasicAuthUsers.Select(user =>
            {
                return user.ToWire();
            }));
        }

        if (IpAccess is not null)
        {
            wire.IpAccess = IpAccess.ToWire();
        }

        // Both wire fields stay zero when the site is not limited: the agent reads a zero rate as "no
        // limit" and refuses a burst that comes without a rate.
        if (RequestRate is not null)
        {
            wire.RequestRatePerSecond = RequestRate.PerSecond;
            wire.RequestBurst = RequestRate.Burst;
        }

        return wire;
    }
}
