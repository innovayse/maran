using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Persistence;

/// <summary>
/// The product's first tenant query filter, tested as the thing it is: one database holding two
/// customers' rows, and two contexts that must not see each other's (spec §8).
/// </summary>
/// <remarks>
/// Every test here opens the SAME in-memory database from two principals. Seeding through a
/// separate context per tenant would let each test's own setup do the separating, and the filter
/// could be deleted without a single assertion changing (rules/testing.md — a test's setup must not
/// do the work the production path is supposed to do).
/// </remarks>
public sealed class SitesDbContextTenantFilterTests
{
    /// <summary>A customer cannot see another tenants site.</summary>
    [Fact]
    public async Task A_customer_cannot_see_another_tenants_site()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var visible = await context.Sites.Select(site => site.Domain).ToListAsync();

        Assert.Equal(["mine.example.com"], visible);
    }

    /// <summary>Another tenants site is absent rather than forbidden when read by id.</summary>
    [Fact]
    public async Task Another_tenants_site_is_absent_rather_than_forbidden_when_read_by_id()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var theirSiteId = await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await context.Sites.SingleOrDefaultAsync(site => site.Id == theirSiteId);

        // Not "found but refused": the row is not in the result set at all, which is what makes a
        // 404 the honest answer and denies a caller the existence oracle a 403 would give them.
        Assert.Null(found);
    }

    /// <summary>An administrator sees every tenants sites.</summary>
    [Fact]
    public async Task An_administrator_sees_every_tenants_sites()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var visible = await context.Sites.Select(site => site.Domain).OrderBy(domain => domain).ToListAsync();

        Assert.Equal(["mine.example.com", "theirs.example.com"], visible);
    }

    /// <summary>A customer with no account sees no sites at all.</summary>
    [Fact]
    public async Task A_customer_with_no_account_sees_no_sites_at_all()
    {
        // A principal that is neither an administrator nor bound to an account must fall to the
        // closed side of the filter. If the comparison were written so that a null account id
        // matched a null column, an unbound principal would become a wildcard.
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, Guid.NewGuid(), Guid.NewGuid());

        await using var context = SitesTestContext.Create(
            new FakeCurrentUser(Guid.NewGuid(), accountId: null, isAdmin: false),
            database);
        var visible = await context.Sites.ToListAsync();

        Assert.Empty(visible);
    }

    /// <summary>Ignoring the query filter is what it takes to see another tenants row.</summary>
    [Fact]
    public async Task Ignoring_the_query_filter_is_what_it_takes_to_see_another_tenants_row()
    {
        // Guards the tests above from passing vacuously: if the seed never wrote the other
        // tenant's site, "cannot see it" would be true for the wrong reason.
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var theirSiteId = await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await context.Sites.IgnoreQueryFilters().SingleOrDefaultAsync(site => site.Id == theirSiteId);

        Assert.NotNull(found);
    }

    /// <summary>Writes one site for each of two accounts into one database, as an administrator.</summary>
    /// <param name="database">The in-memory database name shared by every context in the test.</param>
    /// <param name="mine">The account the test's customer owns.</param>
    /// <param name="theirs">The other tenant's account.</param>
    /// <returns>The id of the OTHER tenant's site.</returns>
    private static async Task<Guid> SeedAsync(string database, Guid mine, Guid theirs)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var theirSite = SitesTestContext.PhpSite(theirs, "theirs.example.com");
        seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com"));
        seed.Sites.Add(theirSite);
        await seed.SaveChangesAsync();
        return theirSite.Id;
    }
}
