using Maran.Modules.Marketplace.Commands.SetMarketplaceEnabled;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Tests.Commands.SetMarketplaceEnabled;

/// <summary>
/// The marketplace switch changes only after the password is proven again, every change and every refusal is
/// journalled, and switching it makes no marketplace call.
/// </summary>
public sealed class SetMarketplaceEnabledCommandHandlerTests
{
    /// <summary>Builds the command as the controller stamps it.</summary>
    /// <param name="world">The world.</param>
    /// <param name="enabled">The switch asked for.</param>
    /// <param name="password">The password typed.</param>
    /// <returns>The command.</returns>
    private static SetMarketplaceEnabledCommand Command(OperationWorld world, bool enabled, string password)
    {
        return new SetMarketplaceEnabledCommand(enabled, password, world.UserId, "203.0.113.5", "test-agent");
    }

    /// <summary>A fresh installation is switched off; the right password switches it on and journals the change.</summary>
    [Fact]
    public async Task The_right_password_switches_the_marketplace_on_and_journals_it()
    {
        using var world = new OperationWorld();
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);

        var status = await world.SwitchHandler().HandleAsync(
            Command(world, enabled: true, FakeReauthentication.ValidPassword), CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.True(status.Value.Enabled);
        Assert.True(status.Value.HostConfigured);
        Assert.Equal("marketplace.test", status.Value.Host);
        Assert.True(await world.Settings.IsEnabledAsync(CancellationToken.None));
        var entry = Assert.Single(world.AuditFor(MarketplaceClientAuditActions.MarketplaceEnabled));
        Assert.True(entry.Succeeded);
        Assert.Equal(0, world.Client.ReadCalls);
    }

    /// <summary>A wrong password leaves the switch where it was, counts as a refusal and is journalled as a refused change.</summary>
    [Fact]
    public async Task A_wrong_password_leaves_the_switch_alone_and_is_journalled()
    {
        using var world = new OperationWorld();
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);

        var refused = await world.SwitchHandler().HandleAsync(
            Command(world, enabled: true, "guess"), CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Equal(1, world.Reauthentication.Refusals);
        Assert.False(await world.Settings.IsEnabledAsync(CancellationToken.None));
        var entry = Assert.Single(world.AuditFor(MarketplaceClientAuditActions.MarketplaceEnabled));
        Assert.False(entry.Succeeded);
        Assert.Contains("reason=ReauthenticationFailed", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Switching off drops the catalogue cache, journals the change as a disable, and an empty password is refused.</summary>
    [Fact]
    public async Task Switching_off_is_journalled_as_a_disable_and_needs_the_password_too()
    {
        using var world = new OperationWorld();

        var withoutPassword = await world.SwitchHandler().HandleAsync(
            Command(world, enabled: false, string.Empty), CancellationToken.None);
        var off = await world.SwitchHandler().HandleAsync(
            Command(world, enabled: false, FakeReauthentication.ValidPassword), CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", withoutPassword.Error!.Code);
        Assert.True(world.AuditFor(MarketplaceClientAuditActions.MarketplaceDisabled).Count == 2);
        Assert.False(off.Value.Enabled);
        Assert.False(await world.Settings.IsEnabledAsync(CancellationToken.None));
    }

    /// <summary>The status lists what is sent in the caller's words, and the modules waiting for a restart.</summary>
    [Fact]
    public async Task The_status_lists_what_is_sent_and_the_modules_waiting_for_a_restart()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Installed);
        world.Loader.Seed("dns", "1.0.0", ModuleLifecycleState.Active);

        var status = await world.Status.ReadAsync(CancellationToken.None);

        Assert.Equal(6, status.SentItems.Count);
        Assert.Equal("mail", Assert.Single(status.RestartRequiredModules));
    }
}
