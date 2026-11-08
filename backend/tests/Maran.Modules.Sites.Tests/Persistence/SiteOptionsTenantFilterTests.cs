using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Persistence;

/// <summary>
/// The tenant filter over a site's options and the rows under them, tested the way
/// <see cref="SitesDbContextTenantFilterTests"/> tests the sites: one database, two principals, so only
/// the filter separates the rows. Options carry no account id of their own, which is exactly why each
/// level has to be filtered through its parent.
/// </summary>
public sealed class SiteOptionsTenantFilterTests
{
    /// <summary>A customer sees their own site's options and none of another tenants.</summary>
    [Fact]
    public async Task A_customer_sees_their_own_sites_options_and_none_of_another_tenants()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        Assert.Single(await context.SiteOptions.ToListAsync());
        Assert.Single(await context.Set<Maran.Modules.Sites.Domain.Entities.SiteRedirect>().ToListAsync());
        Assert.Single(await context.Set<Maran.Modules.Sites.Domain.Entities.SiteBasicAuthUser>().ToListAsync());
        Assert.Single(await context.Set<Maran.Modules.Sites.Domain.Entities.SiteIpRule>().ToListAsync());
    }

    /// <summary>An administrator sees every tenants options.</summary>
    [Fact]
    public async Task An_administrator_sees_every_tenants_options()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, Guid.NewGuid(), Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await context.SiteOptions.CountAsync());
        Assert.Equal(2, await context.Set<Maran.Modules.Sites.Domain.Entities.SiteBasicAuthUser>().CountAsync());
    }

    /// <summary>A reseller, who owns no site, sees no options.</summary>
    [Fact]
    public async Task A_reseller_who_owns_no_site_sees_no_options()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, Guid.NewGuid(), Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Reseller(Guid.NewGuid()), database);

        Assert.Empty(await context.SiteOptions.ToListAsync());
        Assert.Empty(await context.Set<Maran.Modules.Sites.Domain.Entities.SiteBasicAuthUser>().ToListAsync());
    }

    /// <summary>Loading a site loads its options and their rows with it.</summary>
    [Fact]
    public async Task Loading_a_site_loads_its_options_and_their_rows_with_it()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        await SeedAsync(database, mine, Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var site = await context.Sites.SingleAsync();

        // No Include anywhere: a handler that loads a site and re-renders its vhost must not be able
        // to forget the options.
        Assert.NotNull(site.Options);
        Assert.Single(site.Options.Redirects);
        Assert.Single(site.Options.BasicAuthUsers);
        Assert.Single(site.Options.IpRules);
    }

    /// <summary>Seeds two tenants' sites, each with a full set of options.</summary>
    /// <param name="database">The in-memory database name to seed.</param>
    /// <param name="mine">The first tenant's account.</param>
    /// <param name="theirs">The second tenant's account.</param>
    private static async Task SeedAsync(string database, Guid mine, Guid theirs)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        foreach (var (account, domain) in new[] { (mine, "mine.example.com"), (theirs, "theirs.example.com") })
        {
            var site = SitesTestContext.PhpSite(account, domain);
            site.ReplaceOptions(SiteOptionsSamples.Full());
            seed.Sites.Add(site);
        }

        await seed.SaveChangesAsync();
    }
}
