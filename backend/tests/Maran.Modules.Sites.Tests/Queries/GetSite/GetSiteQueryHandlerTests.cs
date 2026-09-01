using Maran.Modules.Sites.Queries.GetSite;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.GetSite;

/// <summary>Behavioral contract of <see cref="GetSiteQueryHandler"/>.</summary>
public sealed class GetSiteQueryHandlerTests
{
    /// <summary>Reading ones own site returns its detail including the document root.</summary>
    [Fact]
    public async Task Reading_ones_own_site_returns_its_detail_including_the_document_root()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await new GetSiteQueryHandler(context).HandleAsync(
            new GetSiteQuery(siteId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mine.example.com", result.Value.Domain);
        Assert.Equal("/home/acct/sites/mine.example.com", result.Value.DocumentRoot);
        Assert.False(result.Value.HasCertificate);
    }

    /// <summary>Reading a site returns the aliases it was created with.</summary>
    [Fact]
    public async Task Reading_a_site_returns_the_aliases_it_was_created_with()
    {
        // The detail view is where an operator confirms what a site actually serves; aliases
        // silently dropped here read as a site that answers on one hostname when it answers on three.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com", "www.mine.example.com", "cdn.mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await new GetSiteQueryHandler(context).HandleAsync(
            new GetSiteQuery(siteId),
            CancellationToken.None);

        Assert.Equal(["www.mine.example.com", "cdn.mine.example.com"], result.Value.Aliases);
    }

    /// <summary>Reading another tenants site answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Reading_another_tenants_site_answers_not_found_rather_than_forbidden()
    {
        // Definition of Done item 3. The code's suffix is what drives the status: "SiteNotFound"
        // maps to 404 in ApiResultExtensions, and there is no "...Forbidden" code on this path at
        // all — 403 would confirm the site exists.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await new GetSiteQueryHandler(context).HandleAsync(
            new GetSiteQuery(theirSiteId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.EndsWith("NotFound", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>An administrator can read any tenants site.</summary>
    [Fact]
    public async Task An_administrator_can_read_any_tenants_site()
    {
        // Guards the test above from passing because the seed never happened: the same id, read by
        // a principal the filter does not narrow, is found.
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, theirs, "theirs.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var result = await new GetSiteQueryHandler(context).HandleAsync(
            new GetSiteQuery(siteId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Seeds one site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, string domain, params string[] aliases)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, domain, "8.3", aliases);
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }
}
