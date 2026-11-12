using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Reading a site again once a job holds it.</summary>
public sealed class SiteRowRefreshTests
{
    /// <summary>The read carries the options saved after an earlier copy of the site was loaded.</summary>
    [Fact]
    public async Task The_read_carries_options_saved_after_an_earlier_copy_was_loaded()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var site = SitesTestContext.PhpSite(account, "a.example");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.Add(site);
        await context.SaveChangesAsync();
        var listed = await context.Sites.SingleAsync();
        Assert.Null(listed.Options);
        await using (var other = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            (await other.Sites.SingleAsync()).ReplaceOptions(SiteOptionsSamples.Full());
            await other.SaveChangesAsync();
        }

        var fresh = await SiteRowRefresh.ReadAsync(context, site.Id, account, CancellationToken.None);

        Assert.Single(fresh!.Options!.BasicAuthUsers);
        Assert.Single(fresh.Options.Redirects);
        Assert.Single(fresh.Options.IpRules);
    }

    /// <summary>The read is not tracked, so it can never be mistaken for the stale copy the context still holds.</summary>
    [Fact]
    public async Task The_read_is_not_tracked()
    {
        var account = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var site = SitesTestContext.PhpSite(account, "a.example");
        context.Sites.Add(site);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var fresh = await SiteRowRefresh.ReadAsync(context, site.Id, account, CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A site deleted since it was listed reads as absent.</summary>
    [Fact]
    public async Task A_deleted_site_reads_as_absent()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());

        Assert.Null(await SiteRowRefresh.ReadAsync(context, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>
    /// A site of another account is never returned, even by its id: the read bypasses the principal, not the
    /// tenant boundary. The accepting control is the same id read for its own account.
    /// </summary>
    [Fact]
    public async Task A_site_of_another_account_reads_as_absent_and_its_own_account_reads_it()
    {
        var owner = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin());
        var site = SitesTestContext.PhpSite(owner, "a.example");
        context.Sites.Add(site);
        await context.SaveChangesAsync();

        Assert.Null(await SiteRowRefresh.ReadAsync(context, site.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.NotNull(await SiteRowRefresh.ReadAsync(context, site.Id, owner, CancellationToken.None));
    }
}
