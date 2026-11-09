using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>
/// A site's per-site options: canonical host, upload size, cache headers, redirects, basic
/// authentication, IP rules and request limit. At most one per site, replaced as a whole.
/// </summary>
/// <remarks>
/// <para>
/// Like the rest of the site, these are rendered into a vhost the agent writes, and the agent keeps no
/// copy: this row is the definition and the vhost a rendering of it. Every re-render therefore has to
/// carry them, which is why <see cref="Site"/> loads them with itself and the descriptor mappers read
/// them from it.
/// </para>
/// <para>
/// <see cref="Replace"/> is the only mutator. A partial edit would need rules about which pieces may
/// change alone, and the one caller replaces the whole set; the caps are enforced here as well as by
/// the validator, because a caller that skipped validation must not be able to store an unbounded set.
/// </para>
/// </remarks>
public sealed class SiteOptions
{
    /// <summary>The redirect rules the options hold. The backing field EF Core reads and writes.</summary>
    private readonly List<SiteRedirect> _redirects = [];

    /// <summary>The basic-authentication users the options hold. The backing field EF Core reads and writes.</summary>
    private readonly List<SiteBasicAuthUser> _basicAuthUsers = [];

    /// <summary>The IP rules the options hold. The backing field EF Core reads and writes.</summary>
    private readonly List<SiteIpRule> _ipRules = [];

    /// <summary>The site these options belong to; also their key, since a site has at most one set.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The site, as the relationship EF Core cascades on and the tenant filter reads through.</summary>
    public Site Site { get; private set; }

    /// <summary>Which of the apex / "www." pair is canonical.</summary>
    public CanonicalHost CanonicalHost { get; private set; }

    /// <summary>The largest request body in MiB, or null for the web server's default.</summary>
    public int? ClientMaxBodySizeMib { get; private set; }

    /// <summary>How many days browsers may cache static files, or null for no cache headers.</summary>
    public int? StaticCacheDays { get; private set; }

    /// <summary>
    /// Whether <see cref="IpRules"/> is an allow-list or a deny-list; null exactly when there are no
    /// rules, because a mode with nothing to apply it to would be a value that means nothing.
    /// </summary>
    public IpAccessMode? IpAccessMode { get; private set; }

    /// <summary>
    /// How fast one visitor address may request the site, or null for no limit. Stored as the two
    /// columns of an owned value, both set or both null.
    /// </summary>
    public SiteRequestRate? RequestRate { get; private set; }

    /// <summary>The redirect rules, in the order they are rendered.</summary>
    public IReadOnlyList<SiteRedirect> Redirects
    {
        get
        {
            return _redirects;
        }
    }

    /// <summary>The basic-authentication users; empty means no authentication.</summary>
    public IReadOnlyList<SiteBasicAuthUser> BasicAuthUsers
    {
        get
        {
            return _basicAuthUsers;
        }
    }

    /// <summary>The addresses or ranges the access mode applies to.</summary>
    public IReadOnlyList<SiteIpRule> IpRules
    {
        get
        {
            return _ipRules;
        }
    }

    /// <summary>Creates an empty set of options for <paramref name="site"/>.</summary>
    /// <param name="site">The site the options belong to.</param>
    public SiteOptions(Site site)
    {
        Site = site;
        SiteId = site.Id;
        CanonicalHost = CanonicalHost.None;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteOptions()
    {
        Site = null!;
    }

    /// <summary>Replaces every option with the given set.</summary>
    /// <param name="values">The complete new set; anything not in it is removed.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A list is longer than its cap in <see cref="SiteOptionsLimits"/>. The validator refuses such a
    /// request with a code before it gets here, so reaching this is a caller that skipped validation.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// An IP mode is given with no rules, or rules with no mode. The validator refuses both.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A request rate or burst is outside its bounds in <see cref="SiteOptionsLimits"/>. The validator
    /// refuses such a request with a code first; the bounds are held here too because the web server's
    /// configuration is written from this value.
    /// </exception>
    public void Replace(SiteOptionsValues values)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Redirects.Count, SiteOptionsLimits.MaximumRedirects);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.BasicAuthUsers.Count, SiteOptionsLimits.MaximumBasicAuthUsers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.IpRules.Count, SiteOptionsLimits.MaximumIpRules);
        if (values.RequestRate is { } rate)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(rate.PerSecond, SiteOptionsLimits.MinimumRequestsPerSecond);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rate.PerSecond, SiteOptionsLimits.MaximumRequestsPerSecond);
            ArgumentOutOfRangeException.ThrowIfLessThan(rate.Burst, SiteOptionsLimits.MinimumRequestBurst);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rate.Burst, SiteOptionsLimits.MaximumRequestBurst);
        }

        if ((values.IpAccessMode is null) != (values.IpRules.Count == 0))
        {
            throw new ArgumentException("An IP access mode is set exactly when there are IP rules.", nameof(values));
        }

        CanonicalHost = values.CanonicalHost;
        ClientMaxBodySizeMib = values.ClientMaxBodySizeMib;
        StaticCacheDays = values.StaticCacheDays;
        IpAccessMode = values.IpAccessMode;
        RequestRate = values.RequestRate;

        _redirects.Clear();
        for (var index = 0; index < values.Redirects.Count; index++)
        {
            var redirect = values.Redirects[index];
            _redirects.Add(new SiteRedirect(this, index, redirect.Path, redirect.Match, redirect.Target, redirect.Status));
        }

        _basicAuthUsers.Clear();
        for (var index = 0; index < values.BasicAuthUsers.Count; index++)
        {
            var user = values.BasicAuthUsers[index];
            _basicAuthUsers.Add(new SiteBasicAuthUser(this, index, user.Name, user.PasswordHash));
        }

        _ipRules.Clear();
        for (var index = 0; index < values.IpRules.Count; index++)
        {
            _ipRules.Add(new SiteIpRule(this, index, values.IpRules[index]));
        }
    }
}
