using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// Which sites an installed app is still running, as the app store's uninstall guard asks it. The answer must
/// be server-wide (a tenant-filtered one lets an administrator remove a runtime another customer depends on)
/// and exact (a site on another version must not block, a site on this one must).
/// </summary>
public sealed class SiteRuntimeUsageTests
{
    /// <summary>The first customer account.</summary>
    private static readonly Guid FirstAccount = Guid.NewGuid();

    /// <summary>The second customer account.</summary>
    private static readonly Guid SecondAccount = Guid.NewGuid();

    /// <summary>Builds a Node site.</summary>
    private static Site NodeSite(Guid accountId, string domain, string nodeVersion)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Node,
            string.Empty,
            string.Empty,
            nodeVersion,
            3100,
            "node server.js",
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Builds a static site, which uses no runtime.</summary>
    private static Site StaticSite(Guid accountId, string domain)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Static,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Seeds both accounts' sites and opens the usage reader as an administrator.</summary>
    private static async Task<SiteRuntimeUsage> OpenAsync(string database)
    {
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(FirstAccount, "b.example.com", "8.3"));
            seed.Sites.Add(SitesTestContext.PhpSite(SecondAccount, "a.example.com", "8.3"));
            seed.Sites.Add(SitesTestContext.PhpSite(SecondAccount, "c.example.com", "8.4"));
            seed.Sites.Add(NodeSite(FirstAccount, "n.example.com", "22"));
            seed.Sites.Add(StaticSite(SecondAccount, "s.example.com"));
            await seed.SaveChangesAsync();
        }

        return new SiteRuntimeUsage(SitesTestContext.Create(FakeCurrentUser.Admin(), database));
    }

    /// <summary>The web server serves every site, whatever its backend, across accounts, in order.</summary>
    [Fact]
    public async Task The_web_server_serves_every_site_whatever_its_backend_across_accounts_in_order()
    {
        var usage = await OpenAsync(Guid.NewGuid().ToString());

        Assert.Equal(
            ["a.example.com", "b.example.com", "c.example.com", "n.example.com", "s.example.com"],
            await usage.ListDomainsOnWebServerAsync(CancellationToken.None));
    }

    /// <summary>An empty host has no site on the web server.</summary>
    [Fact]
    public async Task An_empty_host_has_no_site_on_the_web_server()
    {
        var usage = new SiteRuntimeUsage(SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString()));

        Assert.Empty(await usage.ListDomainsOnWebServerAsync(CancellationToken.None));
    }

    /// <summary>PHP usage lists the sites bound to that version of every account, and only those.</summary>
    [Fact]
    public async Task Php_usage_lists_the_sites_bound_to_that_version_of_every_account_and_only_those()
    {
        var usage = await OpenAsync(Guid.NewGuid().ToString());

        Assert.Equal(["a.example.com", "b.example.com"], await usage.ListDomainsOnPhpAsync("8.3", CancellationToken.None));
        Assert.Equal(["c.example.com"], await usage.ListDomainsOnPhpAsync("8.4", CancellationToken.None));
        Assert.Empty(await usage.ListDomainsOnPhpAsync("7.4", CancellationToken.None));
    }

    /// <summary>Node usage lists the Node sites on that major and ignores PHP and static sites.</summary>
    [Fact]
    public async Task Node_usage_lists_the_node_sites_on_that_major_and_ignores_php_and_static_sites()
    {
        var usage = await OpenAsync(Guid.NewGuid().ToString());

        Assert.Equal(["n.example.com"], await usage.ListDomainsOnNodeAsync("22", CancellationToken.None));
        Assert.Empty(await usage.ListDomainsOnNodeAsync("20", CancellationToken.None));

        // The inverse control: an empty version string is what a PHP or static site stores in its Node column,
        // and it must not make every one of them look like a Node site.
        Assert.Empty(await usage.ListDomainsOnNodeAsync(string.Empty, CancellationToken.None));
    }

    /// <summary>The answer is server-wide: a customer principal does not narrow it to their own sites.</summary>
    [Fact]
    public async Task The_answer_is_server_wide_a_customer_principal_does_not_narrow_it_to_their_own_sites()
    {
        var database = Guid.NewGuid().ToString();
        await OpenAsync(database);
        var asCustomer = new SiteRuntimeUsage(SitesTestContext.Create(FakeCurrentUser.Customer(FirstAccount), database));

        Assert.Equal(["a.example.com", "b.example.com"], await asCustomer.ListDomainsOnPhpAsync("8.3", CancellationToken.None));
    }
}
