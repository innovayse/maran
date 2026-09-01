using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="SitesDbContext"/> instances for a named tenant, plus the sites to
/// seed them with. Each context gets its own uniquely-named in-memory database unless a caller
/// passes a shared name, which is what an isolation test needs: two contexts, two principals, ONE
/// database, so the only thing separating the rows is the query filter under test.
/// </summary>
public static class SitesTestContext
{
    /// <summary>Creates a context over a fresh database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    public static SitesDbContext Create(ICurrentUser currentUser, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new SitesDbContext(options, currentUser);
    }

    /// <summary>Builds a PHP-backed site row for <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="phpVersion">The bound PHP version.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    public static Site PhpSite(
        Guid accountId,
        string domain,
        string phpVersion = "8.3",
        params string[] aliases)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            aliases,
            SiteBackendType.Php,
            phpVersion,
            string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
