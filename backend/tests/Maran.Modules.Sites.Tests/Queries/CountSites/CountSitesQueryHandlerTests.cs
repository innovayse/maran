using Maran.Modules.Sites.Queries.CountSites;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.CountSites;

/// <summary>Behavioral contract of <see cref="CountSitesQueryHandler"/>.</summary>
public sealed class CountSitesQueryHandlerTests
{
    /// <summary>An administrator counts every site, more than one page of them included.</summary>
    /// <remarks>
    /// Thirty rows, above the list's default page of twenty-five, so a count read off a page instead
    /// of asked as a count would answer 25 here.
    /// </remarks>
    [Fact]
    public async Task An_administrator_counts_every_site_beyond_one_page()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.AddRange(Enumerable.Range(0, 30).Select(index =>
            {
                return SitesTestContext.PhpSite(Guid.NewGuid(), $"site{index}.example.com");
            }));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new CountSitesQueryHandler(context)
            .HandleAsync(new CountSitesQuery(), CancellationToken.None);

        Assert.Equal(30, result.Value);
    }

    /// <summary>A customer's count is of their own sites only.</summary>
    [Fact]
    public async Task A_customers_count_is_of_their_own_sites_only()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs.example.com"));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new CountSitesQueryHandler(context)
            .HandleAsync(new CountSitesQuery(), CancellationToken.None);

        Assert.Equal(1, result.Value);
    }
}
