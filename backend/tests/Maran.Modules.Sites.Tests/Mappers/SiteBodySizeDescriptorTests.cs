using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>
/// The request body limit a vhost is described with (issue #556): the plan's upload bound for a PHP
/// site that stated none, the stated figure within the bound, and no plan involvement for other sites.
/// Each rule is paired with the control that fails when the rule is a literal.
/// </summary>
public sealed class SiteBodySizeDescriptorTests
{
    /// <summary>A plan that bounds uploads at 128 MiB, distinct from the default plan's 64.</summary>
    private static readonly PhpLimitsSnapshot Plan128 = new(256, 128, 120, 3000);

    /// <summary>A PHP site that stated no figure is described with the plan's bound.</summary>
    [Fact]
    public void A_php_site_that_stated_no_figure_is_described_with_the_plans_bound()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        var descriptor = SiteDescriptorMapper.From(site, SiteWebServer.Nginx, Plan128);

        Assert.Equal(128u, descriptor.Options.ClientMaxBodySizeMib);
    }

    /// <summary>The same site on another plan is described with that plan's bound (inverse control: not a literal).</summary>
    [Fact]
    public void The_same_site_on_another_plan_is_described_with_that_plans_bound()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        var descriptor = SiteDescriptorMapper.From(site, SiteWebServer.Nginx, new PhpLimitsSnapshot(256, 32, 120, 3000));

        Assert.Equal(32u, descriptor.Options.ClientMaxBodySizeMib);
    }

    /// <summary>A stated figure within the bound wins over the bound.</summary>
    [Fact]
    public void A_stated_figure_within_the_bound_wins_over_the_bound()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Empty() with { ClientMaxBodySizeMib = 10 });

        var descriptor = SiteDescriptorMapper.From(site, SiteWebServer.Nginx, Plan128);

        Assert.Equal(10u, descriptor.Options.ClientMaxBodySizeMib);
    }

    /// <summary>A stored figure above the bound is cut to it, and the row keeps the figure.</summary>
    [Fact]
    public void A_stored_figure_above_the_bound_is_cut_to_it_and_the_row_keeps_the_figure()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Empty() with { ClientMaxBodySizeMib = 512 });

        var descriptor = SiteDescriptorMapper.From(site, SiteWebServer.Nginx, Plan128);

        Assert.Equal(128u, descriptor.Options.ClientMaxBodySizeMib);
        Assert.Equal(512, site.Options!.ClientMaxBodySizeMib);
    }

    /// <summary>A static site is described without a limit whatever the plan says.</summary>
    [Fact]
    public void A_static_site_is_described_without_a_limit_whatever_the_plan_says()
    {
        var descriptor = SiteDescriptorMapper.From(SiteOf(SiteBackendType.Static), SiteWebServer.Nginx, Plan128);

        Assert.Null(descriptor.Options.ClientMaxBodySizeMib);
    }

    /// <summary>A reverse-proxied site is described without a limit whatever the plan says.</summary>
    [Fact]
    public void A_reverse_proxied_site_is_described_without_a_limit_whatever_the_plan_says()
    {
        var descriptor = SiteDescriptorMapper.From(SiteOf(SiteBackendType.ReverseProxy), SiteWebServer.Nginx, Plan128);

        Assert.Null(descriptor.Options.ClientMaxBodySizeMib);
    }

    /// <summary>The snapshot another module re-renders from carries the same limit as this module's descriptor.</summary>
    [Fact]
    public async Task The_snapshot_another_module_re_renders_from_carries_the_same_limit()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "example.com"));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var snapshot = await SiteDirectories.Over(context, Plan128).FindByDomainAsync("example.com", CancellationToken.None);

        Assert.Equal(128, snapshot!.Options.ClientMaxBodySizeMib);
    }

    /// <summary>Builds a site of the given backend with nothing PHP-specific.</summary>
    /// <param name="backendType">The backend.</param>
    /// <returns>The site.</returns>
    private static Site SiteOf(SiteBackendType backendType)
    {
        return new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.com",
            [],
            backendType,
            string.Empty,
            backendType == SiteBackendType.ReverseProxy ? "127.0.0.1:3000" : string.Empty,
            string.Empty,
            0,
            string.Empty,
            "/home/acct/sites/example.com",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
