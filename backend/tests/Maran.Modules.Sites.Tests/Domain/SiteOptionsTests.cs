using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Domain;

/// <summary>Behavioral contract of <see cref="SiteOptions"/> and of <see cref="Site.ReplaceOptions"/>.</summary>
public sealed class SiteOptionsTests
{
    /// <summary>A site has no options until some are set.</summary>
    [Fact]
    public void A_new_site_has_no_options()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        Assert.Null(site.Options);
    }

    /// <summary>Replacing stores every group the set states.</summary>
    [Fact]
    public void Replacing_stores_every_group_the_set_states()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        site.ReplaceOptions(SiteOptionsSamples.Full());

        var options = Assert.IsType<SiteOptions>(site.Options);
        Assert.Equal(site.Id, options.SiteId);
        Assert.Equal(CanonicalHost.Www, options.CanonicalHost);
        Assert.Equal(64, options.ClientMaxBodySizeMib);
        Assert.Equal(30, options.StaticCacheDays);
        Assert.Equal("/old", Assert.Single(options.Redirects).Path);
        Assert.Equal("alice", Assert.Single(options.BasicAuthUsers).Name);
        Assert.Equal(IpAccessMode.AllowList, options.IpAccessMode);
        Assert.Equal("203.0.113.0/24", Assert.Single(options.IpRules).Cidr);
        Assert.Equal(new SiteRequestRate(10, 20), options.RequestRate);
    }

    /// <summary>Replacing again removes what the new set does not state.</summary>
    [Fact]
    public void Replacing_again_removes_what_the_new_set_does_not_state()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());
        var first = site.Options;

        site.ReplaceOptions(SiteOptionsSamples.Empty());

        // The same options object is reused, so the key never changes and a second row never exists.
        Assert.Same(first, site.Options);
        Assert.Equal(CanonicalHost.None, site.Options!.CanonicalHost);
        Assert.Null(site.Options.ClientMaxBodySizeMib);
        Assert.Empty(site.Options.Redirects);
        Assert.Empty(site.Options.BasicAuthUsers);
        Assert.Null(site.Options.IpAccessMode);
        Assert.Empty(site.Options.IpRules);
        Assert.Null(site.Options.RequestRate);
    }

    /// <summary>Redirects keep the order they were stated in.</summary>
    [Fact]
    public void Redirects_keep_the_order_they_were_stated_in()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var values = SiteOptionsSamples.Empty() with
        {
            Redirects =
            [
                new SiteRedirectValue("/a", RedirectMatch.Exact, "/b", RedirectStatus.Permanent),
                new SiteRedirectValue("/c/", RedirectMatch.Prefix, "/d/", RedirectStatus.Temporary),
            ],
        };

        site.ReplaceOptions(values);

        var ordered = site.Options!.Redirects
            .OrderBy(rule =>
            {
                return rule.Position;
            })
            .ToList();
        Assert.Equal(["/a", "/c/"], ordered.Select(rule =>
        {
            return rule.Path;
        }));
        Assert.Equal([0, 1], ordered.Select(rule =>
        {
            return rule.Position;
        }));
    }

    /// <summary>More redirect rules than the cap are refused by the entity itself.</summary>
    [Fact]
    public void More_redirect_rules_than_the_cap_are_refused_by_the_entity_itself()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var redirects = Enumerable.Range(0, SiteOptionsLimits.MaximumRedirects + 1)
            .Select(index =>
            {
                return new SiteRedirectValue($"/p{index}", RedirectMatch.Exact, "/x", RedirectStatus.Permanent);
            })
            .ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { Redirects = redirects });
        });
    }

    /// <summary>More users than the cap are refused by the entity itself.</summary>
    [Fact]
    public void More_users_than_the_cap_are_refused_by_the_entity_itself()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var users = Enumerable.Range(0, SiteOptionsLimits.MaximumBasicAuthUsers + 1)
            .Select(index =>
            {
                return new SiteBasicAuthUserValue($"u{index}", SiteOptionsSamples.StoredHash);
            })
            .ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { BasicAuthUsers = users });
        });
    }

    /// <summary>More IP rules than the cap are refused by the entity itself.</summary>
    [Fact]
    public void More_ip_rules_than_the_cap_are_refused_by_the_entity_itself()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var rules = Enumerable.Range(0, SiteOptionsLimits.MaximumIpRules + 1)
            .Select(index =>
            {
                return $"10.0.{index}.0/24";
            })
            .ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { IpAccessMode = IpAccessMode.DenyList, IpRules = rules });
        });
    }

    /// <summary>A request rate or burst outside its bounds is refused by the entity itself.</summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1001, 5)]
    [InlineData(5, -1)]
    [InlineData(5, 1001)]
    public void A_request_rate_or_burst_outside_its_bounds_is_refused_by_the_entity_itself(int perSecond, int burst)
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { RequestRate = new SiteRequestRate(perSecond, burst) });
        });
    }

    /// <summary>The bounds of the request limit are accepted by the entity (the inverse control).</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1000, 1000)]
    public void The_bounds_of_the_request_limit_are_accepted_by_the_entity(int perSecond, int burst)
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        site.ReplaceOptions(SiteOptionsSamples.Empty() with { RequestRate = new SiteRequestRate(perSecond, burst) });

        Assert.Equal(new SiteRequestRate(perSecond, burst), site.Options!.RequestRate);
    }

    /// <summary>A mode with no rules, and rules with no mode, are both refused.</summary>
    [Fact]
    public void A_mode_with_no_rules_and_rules_with_no_mode_are_both_refused()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        Assert.Throws<ArgumentException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { IpAccessMode = IpAccessMode.AllowList });
        });
        Assert.Throws<ArgumentException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { IpRules = ["203.0.113.5/32"] });
        });
    }

    /// <summary>A refused replacement leaves the options as they were.</summary>
    [Fact]
    public void A_refused_replacement_leaves_the_options_as_they_were()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());

        Assert.Throws<ArgumentException>(() =>
        {
            site.ReplaceOptions(SiteOptionsSamples.Empty() with { IpAccessMode = IpAccessMode.AllowList });
        });

        Assert.Equal(CanonicalHost.Www, site.Options!.CanonicalHost);
        Assert.Single(site.Options.Redirects);
    }
}
