using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Persistence;

/// <summary>
/// A site's redirects, basic-auth users and IP rules are mapped as belonging to the site itself, and
/// removing the site removes them.
/// </summary>
/// <remarks>
/// The in-memory provider enforces no constraint, so the database half of the guarantee is asserted
/// on the model the migration is generated from (a foreign key to the site with the cascade
/// behaviour), and the behavioural half on a real delete through the context.
/// </remarks>
public sealed class SiteChildRowsCascadeTests
{
    /// <summary>Each child row is mapped as a foreign key to its site that cascades on delete.</summary>
    [Theory]
    [InlineData(typeof(SiteRedirect))]
    [InlineData(typeof(SiteBasicAuthUser))]
    [InlineData(typeof(SiteIpRule))]
    public void A_child_row_has_a_cascading_foreign_key_to_its_site(Type childType)
    {
        using var context = SitesTestContext.Create(FakeCurrentUser.Admin());

        var foreignKey = Assert.Single(
            context.Model.FindEntityType(childType)!.GetForeignKeys(),
            candidate => { return candidate.PrincipalEntityType.ClrType == typeof(Site); });

        Assert.Equal("SiteId", Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Removing a site removes its redirects, basic-auth users and IP rules.</summary>
    [Fact]
    public async Task Removing_a_site_removes_its_redirects_users_and_ip_rules()
    {
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database);

        await using (var before = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            // The positive control: the rows exist, so "none left" below is a deletion and not a seed
            // that never wrote them.
            Assert.Equal(1, await before.Set<SiteRedirect>().CountAsync());
            Assert.Equal(1, await before.Set<SiteBasicAuthUser>().CountAsync());
            Assert.Equal(1, await before.Set<SiteIpRule>().CountAsync());
        }

        await using (var removal = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var site = await removal.Sites.SingleAsync(candidate => candidate.Id == siteId);
            removal.Sites.Remove(site);
            await removal.SaveChangesAsync();
        }

        await using var after = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await after.Set<SiteRedirect>().ToListAsync());
        Assert.Empty(await after.Set<SiteBasicAuthUser>().ToListAsync());
        Assert.Empty(await after.Set<SiteIpRule>().ToListAsync());
    }

    /// <summary>Seeds one site carrying one row of each kind.</summary>
    /// <param name="database">The in-memory database to write to.</param>
    /// <returns>The site's id.</returns>
    private static async Task<Guid> SeedAsync(string database)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "cascade.example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();

        return site.Id;
    }
}
