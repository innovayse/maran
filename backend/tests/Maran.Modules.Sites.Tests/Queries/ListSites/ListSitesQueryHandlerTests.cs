using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.ListSites;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.ListSites;

/// <summary>Behavioral contract of <see cref="ListSitesQueryHandler"/>.</summary>
public sealed class ListSitesQueryHandlerTests
{
    /// <summary>Listing sites returns only the callers own.</summary>
    [Fact]
    public async Task Listing_sites_returns_only_the_callers_own()
    {
        // The handler writes no Where clause at all — the context's tenant filter is what scopes
        // this, which is the point of putting it there rather than in every read (spec §8).
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await new ListSitesQueryHandler(context).HandleAsync(new ListSitesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var domains = result.Value.Select(site =>
        {
            return site.Domain;
        });
        Assert.Equal(["mine.example.com"], domains);
    }

    /// <summary>Listing sites as an administrator returns every tenants.</summary>
    [Fact]
    public async Task Listing_sites_as_an_administrator_returns_every_tenants()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await new ListSitesQueryHandler(context).HandleAsync(new ListSitesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
    }

    /// <summary>Sites are listed oldest first.</summary>
    [Fact]
    public async Task Sites_are_listed_oldest_first()
    {
        // The handler documents an ordering, and a list whose order is documented but unasserted is
        // a list whose order changes silently the next time the query is edited.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(NewSite(account, "second.example.com", 2));
            seed.Sites.Add(NewSite(account, "first.example.com", 1));
            seed.Sites.Add(NewSite(account, "third.example.com", 3));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var result = await new ListSitesQueryHandler(context).HandleAsync(new ListSitesQuery(), CancellationToken.None);

        var domains = result.Value.Select(site =>
        {
            return site.Domain;
        });
        Assert.Equal(["first.example.com", "second.example.com", "third.example.com"], domains);
    }

    /// <summary>Builds a site created on a distinct day, so ordering is unambiguous.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site primary domain.</param>
    /// <param name="day">The day of January 2026 the site was created on.</param>
    private static Site NewSite(Guid accountId, string domain, int day)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/" + domain,
            new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Writes one site for each of two accounts into one database.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="mine">The account the test's customer owns.</param>
    /// <param name="theirs">The other tenant's account.</param>
    private static async Task SeedAsync(string database, Guid mine, Guid theirs)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com"));
        seed.Sites.Add(SitesTestContext.PhpSite(theirs, "theirs.example.com"));
        await seed.SaveChangesAsync();
    }
}
