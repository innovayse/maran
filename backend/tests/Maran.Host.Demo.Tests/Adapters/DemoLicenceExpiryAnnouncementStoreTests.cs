using Maran.Host.Demo.Adapters;
using Maran.Modules.Licensing.Models;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The demo's expiry announcement store is memory: it holds what was announced and touches no file.</summary>
public sealed class DemoLicenceExpiryAnnouncementStoreTests
{
    /// <summary>Nothing is announced until something is remembered.</summary>
    [Fact]
    public async Task Nothing_is_announced_at_first()
    {
        Assert.Null(await new DemoLicenceExpiryAnnouncementStore().ReadAsync(CancellationToken.None));
    }

    /// <summary>What was remembered is read back, replaced by the next, and gone once forgotten.</summary>
    [Fact]
    public async Task A_remembered_announcement_is_read_back_replaced_and_forgotten()
    {
        var store = new DemoLicenceExpiryAnnouncementStore();
        var first = new LicenceExpiryAnnouncement(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), 30);
        var second = new LicenceExpiryAnnouncement(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), 7);

        await store.RememberAsync(first, CancellationToken.None);
        Assert.Equal(first, await store.ReadAsync(CancellationToken.None));

        await store.RememberAsync(second, CancellationToken.None);
        Assert.Equal(second, await store.ReadAsync(CancellationToken.None));

        await store.ForgetAsync(CancellationToken.None);
        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }
}
