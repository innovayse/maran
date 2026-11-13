using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.GetSiteOptions;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

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

        var result = await new GetSiteOptionsQueryHandler(context, Accounts(account), SitesTestContext.SiteNames())
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

        var result = await new GetSiteOptionsQueryHandler(context, Accounts(account), SitesTestContext.SiteNames())
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

        var result = await new GetSiteOptionsQueryHandler(context, Accounts(theirs), SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(theirSiteId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Equal(Maran.SharedKernel.Results.ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>The options answer carries the plan's upload bound, and another bound moves it (inverse control).</summary>
    [Fact]
    public async Task The_options_answer_carries_the_plans_upload_bound()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, withOptions: false);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var small = await new GetSiteOptionsQueryHandler(context, Accounts(account, new PhpLimitsSnapshot(256, 64, 120, 3000)), SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(siteId), CancellationToken.None);
        var large = await new GetSiteOptionsQueryHandler(context, Accounts(account, new PhpLimitsSnapshot(256, 128, 120, 3000)), SitesTestContext.SiteNames())
            .HandleAsync(new GetSiteOptionsQuery(siteId), CancellationToken.None);

        Assert.Equal(64, small.Value.Limits.PlanMaximumBodySizeMib);
        Assert.Equal(128, large.Value.Limits.PlanMaximumBodySizeMib);
    }

    /// <summary>Builds the account window the handler reads the plan through.</summary>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="limits">The plan's PHP bounds, or null for the defaults.</param>
    private static StubAccountDirectory Accounts(Guid accountId, PhpLimitsSnapshot? limits = null)
    {
        return new StubAccountDirectory(new AccountSnapshot(
            accountId,
            "acme",
            MaxSites: 5,
            MaxDatabases: 2,
            MaxSftpUsers: 3,
            MaxCronEntries: 7,
            MaxPhpWorkersPerPool: 10,
            DiskQuotaMb: 1_024, AccountState.Active) with
        { PhpLimits = limits ?? PhpLimitsSnapshot.Default });
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
