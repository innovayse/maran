using Maran.Host.Demo.Adapters;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The demo's licence storage is memory: it holds what was installed and touches no file.</summary>
public sealed class DemoLicenceStoreTests
{
    /// <summary>Nothing is installed until something is.</summary>
    [Fact]
    public void Nothing_is_installed_at_first()
    {
        Assert.Null(new DemoLicenceStore().ReadRawLicenceText());
    }

    /// <summary>What was installed is read back, and a later install replaces it.</summary>
    [Fact]
    public async Task An_installed_licence_is_read_back_and_replaced_by_the_next()
    {
        var store = new DemoLicenceStore();

        await store.InstallAsync("first", CancellationToken.None);
        Assert.Equal("first", store.ReadRawLicenceText());

        await store.InstallAsync("second", CancellationToken.None);
        Assert.Equal("second", store.ReadRawLicenceText());
    }
}
