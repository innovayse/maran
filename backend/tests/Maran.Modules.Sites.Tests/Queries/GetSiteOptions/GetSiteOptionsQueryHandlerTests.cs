using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.GetSiteOptions;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.GetSiteOptions;

/// <summary>Behavioral contract of <see cref="GetSiteOptionsQueryHandler"/>.</summary>
public sealed class GetSiteOptionsQueryHandlerTests
{
    /// <summary>The owner reads the options a replacement stored.</summary>
    [Fact]
    public async Task The_owner_reads_the_options_a_replacement_stored()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, withOptions: true);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await new GetSiteOptionsQueryHandler(context, SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(siteId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CanonicalHost.Www, result.Value.CanonicalHost);
        Assert.Equal(["alice"], result.Value.BasicAuthUsers);
    }

    /// <summary>A site that never had options set answers with the empty set, not with not found.</summary>
    [Fact]
    public async Task A_site_that_never_had_options_set_answers_with_the_empty_set_not_with_not_found()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, withOptions: false);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await new GetSiteOptionsQueryHandler(context, SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(siteId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CanonicalHost.None, result.Value.CanonicalHost);
        Assert.Empty(result.Value.Redirects);
    }

    /// <summary>Another tenants site answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Another_tenants_site_answers_not_found_rather_than_forbidden()
    {
        // The IDOR test (rules/testing.md Definition of Done 3).
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, withOptions: true);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(Guid.NewGuid()), database);

        var result = await new GetSiteOptionsQueryHandler(context, SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(theirSiteId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Equal(Maran.SharedKernel.Results.ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Seeds one site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="withOptions">Whether the site has a full set of options.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, bool withOptions)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, "example.com");
        if (withOptions)
        {
            site.ReplaceOptions(SiteOptionsSamples.Full());
        }

        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }
}
