using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// The Sdk window another module reads sites through, tested for the property the interface promises
/// and the compiler cannot: a cross-module abstraction does not bypass the tenant filter that
/// protects this module's own queries.
/// </summary>
/// <remarks>
/// This is the test rules/architecture.md's "cross-module needs go through Sdk abstractions" needs to
/// be safe rather than merely tidy. A directory that answered from an unfiltered query would hand
/// the Ssl module another customer's site, and the Ssl module — trusting the contract — would issue a
/// certificate for it. Both principals here read the SAME in-memory database, so the only thing
/// separating the rows is the filter under test.
/// </remarks>
public sealed class SiteDirectoryTests
{
    /// <summary>Seeds two customers' sites into one shared database.</summary>
    /// <param name="database">The in-memory database name to seed.</param>
    /// <param name="mine">The account whose site is called mine.example.com.</param>
    /// <param name="theirs">The account whose site is called theirs.example.com.</param>
    private static async Task SeedAsync(string database, Guid mine, Guid theirs)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com"));
        seed.Sites.Add(SitesTestContext.PhpSite(theirs, "theirs.example.com"));
        await seed.SaveChangesAsync();
    }

    /// <summary>Looking up another tenants domain answers null rather than their site.</summary>
    [Fact]
    public async Task Looking_up_another_tenants_domain_answers_null_rather_than_their_site()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        Assert.Null(await new SiteDirectory(context).FindByDomainAsync("theirs.example.com", CancellationToken.None));
    }

    /// <summary>A domain that does not exist and one that belongs to somebody else are indistinguishable.</summary>
    [Fact]
    public async Task A_domain_that_does_not_exist_and_one_that_belongs_to_somebody_else_are_indistinguishable()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var directory = new SiteDirectory(context);

        // Telling them apart would let a caller confirm a domain is hosted here by an account it may
        // not see (rules/security.md — 404, never 403).
        Assert.Null(await directory.FindByDomainAsync("theirs.example.com", CancellationToken.None));
        Assert.Null(await directory.FindByDomainAsync("nowhere.example.com", CancellationToken.None));
    }

    /// <summary>Looking up ones own domain answers the sites facts the renderer needs.</summary>
    [Fact]
    public async Task Looking_up_ones_own_domain_answers_the_sites_facts_the_renderer_needs()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var site = await new SiteDirectory(context).FindByDomainAsync("mine.example.com", CancellationToken.None);

        Assert.NotNull(site);
        Assert.Equal(mine, site.AccountId);
        Assert.Equal(SiteBackend.Php, site.Backend);
        Assert.Equal("8.3", site.PhpVersion);
        Assert.False(site.HasCertificate);
    }

    /// <summary>Attaching a certificate flips the flag every later vhost render is told.</summary>
    [Fact]
    public async Task Attaching_a_certificate_flips_the_flag_every_later_vhost_render_is_told()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var directory = new SiteDirectory(context);
        var site = await directory.FindByDomainAsync("mine.example.com", CancellationToken.None);

        Assert.True(await directory.AttachCertificateAsync(site!.Id, CancellationToken.None));

        await using var reading = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var updated = await reading.Sites.AsNoTracking().SingleAsync(row => row.Id == site.Id);
        Assert.True(updated.HasCertificate);
    }

    /// <summary>Detaching a certificate clears the flag again.</summary>
    [Fact]
    public async Task Detaching_a_certificate_clears_the_flag_again()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, Guid.NewGuid());

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var directory = new SiteDirectory(context);
        var site = await directory.FindByDomainAsync("mine.example.com", CancellationToken.None);
        await directory.AttachCertificateAsync(site!.Id, CancellationToken.None);

        Assert.True(await directory.DetachCertificateAsync(site.Id, CancellationToken.None));

        await using var reading = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var updated = await reading.Sites.AsNoTracking().SingleAsync(row => row.Id == site.Id);
        Assert.False(updated.HasCertificate);
    }

    /// <summary>Flipping the flag on a site that does not exist answers false rather than throwing.</summary>
    [Fact]
    public async Task Flipping_the_flag_on_a_site_that_does_not_exist_answers_false_rather_than_throwing()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var directory = new SiteDirectory(context);

        Assert.False(await directory.AttachCertificateAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.False(await directory.DetachCertificateAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>The unscoped lookup is what renewal needs and it says so in its name.</summary>
    [Fact]
    public async Task The_unscoped_lookup_is_what_renewal_needs_and_it_says_so_in_its_name()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, mine, theirs);

        await using var admin = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var theirSite = await admin.Sites.AsNoTracking().SingleAsync(site => site.AccountId == theirs);

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await new SiteDirectory(context).FindByIdUnscopedAsync(theirSite.Id, CancellationToken.None);

        // Deliberate: renewal runs for the whole server and serves no tenant. It takes an id the
        // caller already holds from its own row, so it cannot be used to enumerate or probe.
        Assert.NotNull(found);
        Assert.Equal(theirs, found.AccountId);
    }
}
