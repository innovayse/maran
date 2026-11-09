using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// The options half of <see cref="SiteDirectory"/>: the snapshot another module re-renders a vhost
/// from carries the site's options, because installing a certificate would otherwise drop them.
/// </summary>
public sealed class SiteDirectoryOptionsTests
{
    /// <summary>A snapshot read by domain carries the stored options without anyone asking for them.</summary>
    [Fact]
    public async Task A_snapshot_read_by_domain_carries_the_stored_options_without_anyone_asking_for_them()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var site = SitesTestContext.PhpSite(account, "example.com");
            site.ReplaceOptions(SiteOptionsSamples.Full());
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var snapshot = await new SiteDirectory(context).FindByDomainAsync("example.com", CancellationToken.None);

        var options = snapshot!.Options;
        Assert.Equal(SiteCanonicalHost.Www, options.CanonicalHost);
        Assert.Equal("/old", Assert.Single(options.Redirects).Path);
        Assert.Equal("alice", Assert.Single(options.BasicAuthUsers).Name);
        Assert.Equal(SiteIpAccessMode.AllowList, options.IpAccess!.Mode);
    }

    /// <summary>A snapshot of a site with no options carries the empty set.</summary>
    [Fact]
    public async Task A_snapshot_of_a_site_with_no_options_carries_the_empty_set()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "example.com"));
            await seed.SaveChangesAsync();
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var snapshot = await new SiteDirectory(context).FindByDomainAsync("example.com", CancellationToken.None);

        Assert.Equal(SiteCanonicalHost.None, snapshot!.Options.CanonicalHost);
        Assert.Empty(snapshot.Options.Redirects);
        Assert.Null(snapshot.Options.IpAccess);
    }

    /// <summary>The unscoped read by id carries the options too.</summary>
    [Fact]
    public async Task The_unscoped_read_by_id_carries_the_options_too()
    {
        var database = Guid.NewGuid().ToString();
        Guid siteId;
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
            site.ReplaceOptions(SiteOptionsSamples.Full());
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
            siteId = site.Id;
        }

        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var snapshot = await new SiteDirectory(context).FindByIdUnscopedAsync(siteId, CancellationToken.None);

        Assert.Equal("alice", Assert.Single(snapshot!.Options.BasicAuthUsers).Name);
    }
}
