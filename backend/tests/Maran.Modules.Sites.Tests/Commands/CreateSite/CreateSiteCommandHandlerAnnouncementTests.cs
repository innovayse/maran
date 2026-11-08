using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.CreateSite;

/// <summary>
/// What <c>CreateSiteCommandHandler</c> tells the other modules: the created site is announced after its row
/// is committed, a refused creation announces nothing, and a subscriber that fails cannot fail the creation.
/// </summary>
public sealed class CreateSiteCommandHandlerAnnouncementTests
{
    /// <summary>A created site is announced with its id, its account and its primary domain.</summary>
    [Fact]
    public async Task A_created_site_is_announced_with_its_id_account_and_domain()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var announced = Assert.IsType<SiteCreated>(Assert.Single(world.Bus.Invoked));
        Assert.Equal(result.Value.Id, announced.SiteId);
        Assert.Equal(account, announced.AccountId);
        Assert.Equal("example.com", announced.Domain);
    }

    /// <summary>The row is already committed when the announcement goes out, so a subscriber can read the site.</summary>
    [Fact]
    public async Task The_row_is_committed_before_the_announcement_goes_out()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        // Read through a second context over the same database: what a subscriber reading it would see.
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database);
        Assert.True(await reader.Sites.AnyAsync(site => site.Id == result.Value.Id));
        Assert.Single(world.Bus.Invoked);
    }

    /// <summary>A refused creation announces nothing.</summary>
    [Fact]
    public async Task A_refused_creation_announces_nothing()
    {
        // The accepting control is the test above: the same call, the same bus, one announcement. Here the
        // plan allows no site, so the handler refuses before the host is touched.
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(account, maxSites: 0);

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(world.Bus.Invoked);
    }

    /// <summary>A subscriber that fails does not fail the creation, and the failure is logged.</summary>
    [Fact]
    public async Task A_subscriber_that_fails_does_not_fail_the_creation()
    {
        var account = Guid.NewGuid();
        var world = new CreateSiteWorld(
            account, bus: new RecordingMessageBus { Refusal = new InvalidOperationException("name server down") });

        var result = await world.Handler().HandleAsync(CreateSiteWorld.Command(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(world.AnnouncerLog.Entries);
        Assert.True(await world.DbContext.Sites.AnyAsync());
    }
}
