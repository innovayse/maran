using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>The switch reads as off on a server that never touched it, and changes only through enable and disable.</summary>
public sealed class MarketplaceSettingsStoreTests
{
    /// <summary>The clock the store reads.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>The store over a database of its own.</summary>
    private readonly MarketplaceSettingsStore _store;

    /// <summary>Builds the store over an isolated in-memory database.</summary>
    public MarketplaceSettingsStoreTests()
    {
        _store = new MarketplaceSettingsStore(MarketplaceDatabase.CreateIsolated(), _clock);
    }

    /// <summary>A server with no settings row reads as off, with nothing written by the read.</summary>
    [Fact]
    public async Task A_server_with_no_settings_row_reads_as_off()
    {
        Assert.False(await _store.IsEnabledAsync(CancellationToken.None));

        var settings = await _store.ReadAsync(CancellationToken.None);
        Assert.False(settings.Enabled);
        Assert.Null(settings.ChangedAt);
    }

    /// <summary>Enabling switches it on and records who and when; reading again sees it.</summary>
    [Fact]
    public async Task Enabling_switches_it_on_and_records_who_and_when()
    {
        var administrator = Guid.NewGuid();

        await _store.EnableAsync(administrator, CancellationToken.None);

        Assert.True(await _store.IsEnabledAsync(CancellationToken.None));
        var settings = await _store.ReadAsync(CancellationToken.None);
        Assert.Equal(administrator, settings.ChangedBy);
        Assert.Equal(_clock.UtcNow, settings.ChangedAt);
    }

    /// <summary>Disabling after enabling switches it off again through the same single row.</summary>
    [Fact]
    public async Task Disabling_after_enabling_switches_it_off_again()
    {
        await _store.EnableAsync(Guid.NewGuid(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = Guid.NewGuid();

        await _store.DisableAsync(second, CancellationToken.None);

        Assert.False(await _store.IsEnabledAsync(CancellationToken.None));
        Assert.Equal(second, (await _store.ReadAsync(CancellationToken.None)).ChangedBy);
    }

    /// <summary>Disabling a switch that was never on records the change and leaves it off.</summary>
    [Fact]
    public async Task Disabling_a_switch_that_was_never_on_leaves_it_off_and_records_the_change()
    {
        var administrator = Guid.NewGuid();

        await _store.DisableAsync(administrator, CancellationToken.None);

        var settings = await _store.ReadAsync(CancellationToken.None);
        Assert.False(settings.Enabled);
        Assert.Equal(administrator, settings.ChangedBy);
    }
}
