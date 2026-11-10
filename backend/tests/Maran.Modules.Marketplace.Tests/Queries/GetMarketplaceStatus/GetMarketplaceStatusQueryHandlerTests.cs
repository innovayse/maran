using Maran.Modules.Marketplace.Queries.GetMarketplaceStatus;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Queries.GetMarketplaceStatus;

/// <summary>The status is what the page shows with the marketplace off, so it makes no marketplace call and reports the switch as it is.</summary>
public sealed class GetMarketplaceStatusQueryHandlerTests
{
    /// <summary>A switched-off marketplace reports itself off with the host and the sent items, and no call is made.</summary>
    [Fact]
    public async Task A_switched_off_marketplace_reports_off_without_calling_it()
    {
        using var world = new OperationWorld();
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);

        var status = await new GetMarketplaceStatusQueryHandler(world.Status)
            .HandleAsync(new GetMarketplaceStatusQuery(), CancellationToken.None);

        Assert.False(status.Value.Enabled);
        Assert.True(status.Value.HostConfigured);
        Assert.Equal("marketplace.test", status.Value.Host);
        Assert.NotEmpty(status.Value.SentItems);
        Assert.Empty(status.Value.RestartRequiredModules);
        Assert.Equal(0, world.Client.ReadCalls);
    }

    /// <summary>A switched-on marketplace reports itself on.</summary>
    [Fact]
    public async Task A_switched_on_marketplace_reports_on()
    {
        using var world = new OperationWorld();

        var status = await new GetMarketplaceStatusQueryHandler(world.Status)
            .HandleAsync(new GetMarketplaceStatusQuery(), CancellationToken.None);

        Assert.True(status.Value.Enabled);
    }
}
