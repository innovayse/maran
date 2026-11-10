using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    /// <param name="failingSave">When set, the context saves through this interceptor, which a test arms to refuse a save.</param>
    public static SitesDbContext Create(
        ICurrentUser currentUser,
        string? databaseName = null,
        FailingSaveInterceptor? failingSave = null)
    {
        var builder = new DbContextOptionsBuilder<SitesDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());
        if (failingSave is not null)
        {
            builder.AddInterceptors(failingSave);
        }

        return new SitesDbContext(builder.Options, currentUser);
    }

    /// <summary>Builds the site display-name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    public static SiteDisplayNames SiteNames()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new SiteDisplayNames(new StringLocalizer<DisplayNames>(factory));
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
            string.Empty,
            0,
            string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
