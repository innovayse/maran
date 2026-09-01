using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What the DATABASE refuses, against real PostgreSQL with the real migrations applied: a hostname
/// claimed by one site cannot be claimed by another, whoever owns it.
/// </summary>
/// <remarks>
/// The handler's pre-check and this key are not the same protection and only one of them closes the
/// race. Two simultaneous creations can both read "not claimed" and both proceed; what stops the
/// second write is the key, and nothing else — which is exactly the reasoning that already puts a
/// unique index on <c>Site.Domain</c>. A test cannot see it anywhere else either: the in-memory
/// provider the unit tests run on enforces no key at all, so a mutation that deleted this
/// constraint would leave every one of them green.
///
/// What is at stake if it is missing: a site whose alias names another tenant's domain wins that
/// name in nginx (a sorted-glob include, the first file parsed answers), serves the victim's
/// <c>/.well-known/acme-challenge/</c> out of its own document root, and can obtain a publicly
/// trusted certificate for a domain it does not own.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteHostnameUniquenessTests : IAsyncLifetime
{
    /// <summary>The name both tenants in these tests try to claim.</summary>
    private const string ContestedName = "victim.example.com";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteHostnameUniquenessTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _pg.CreateAsync();
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The database refuses an alias that names another tenants domain.</summary>
    [Fact]
    public async Task The_database_refuses_an_alias_that_names_another_tenants_domain()
    {
        await SeedAsync(Site(Guid.NewGuid(), ContestedName));

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await SeedAsync(Site(Guid.NewGuid(), "aaa-attacker.example.com", ContestedName));
        });

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
    }

    /// <summary>The database refuses an alias that names another tenants alias.</summary>
    [Fact]
    public async Task The_database_refuses_an_alias_that_names_another_tenants_alias()
    {
        await SeedAsync(Site(Guid.NewGuid(), "owner.example.com", ContestedName));

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await SeedAsync(Site(Guid.NewGuid(), "aaa-attacker.example.com", ContestedName));
        });

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
    }

    /// <summary>Deleting a site releases the hostnames it claimed.</summary>
    [Fact]
    public async Task Deleting_a_site_releases_the_hostnames_it_claimed()
    {
        // The other half of the constraint, and the half that would turn a takeover defence into a
        // denial of service: the claims are removed by the relationship's cascade, so a customer
        // who deletes a site can create it again.
        var first = Site(Guid.NewGuid(), "owner.example.com", ContestedName);
        await SeedAsync(first);

        await using (var context = OpenContext())
        {
            context.Sites.Remove(await context.Sites.SingleAsync(site => site.Id == first.Id));
            await context.SaveChangesAsync();
        }

        await SeedAsync(Site(Guid.NewGuid(), "owner.example.com", ContestedName));

        await using var reread = OpenContext();
        Assert.Equal(2, await reread.SiteHostnames.CountAsync());
    }

    /// <summary>Builds a PHP site claiming a domain and any aliases.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="aliases">Additional hostnames the site answers for.</param>
    /// <returns>The unsaved site.</returns>
    private static Site Site(Guid accountId, string domain, params string[] aliases)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            aliases,
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Writes one site, in its own context, the way two separate requests would.</summary>
    /// <param name="site">The site to insert.</param>
    private async Task SeedAsync(Site site)
    {
        await using var context = OpenContext();
        context.Sites.Add(site);
        await context.SaveChangesAsync();
    }

    /// <summary>Opens a context straight onto this test's database, as an administrator.</summary>
    /// <returns>A context the caller disposes.</returns>
    /// <remarks>
    /// Administrator so that the tenant filter narrows nothing: these tests are about what the
    /// database refuses across accounts, which is a question no filtered read could ask.
    /// </remarks>
    private SitesDbContext OpenContext()
    {
        return new SitesDbContext(
            new DbContextOptionsBuilder<SitesDbContext>().UseNpgsql(_pg.GetConnectionString()).Options,
            new DesignTimeCurrentUser());
    }
}
