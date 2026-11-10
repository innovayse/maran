using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Modules;

/// <summary>
/// The screen-bundle window of the open panel holds no bundle: it describes nothing and opens nothing, and
/// it gives way to a window registered before it.
/// </summary>
public sealed class NullModuleSpaAssetsTests
{
    /// <summary>It describes no module's bundle.</summary>
    [Fact]
    public async Task It_describes_no_bundle()
    {
        Assert.Null(await new NullModuleSpaAssets().DescribeAsync("hello", CancellationToken.None));
    }

    /// <summary>It opens no file.</summary>
    [Fact]
    public async Task It_opens_no_file()
    {
        Assert.Null(await new NullModuleSpaAssets().OpenAsync("hello", "1.0.0", "entry.js", CancellationToken.None));
    }

    /// <summary>The Sdk registers it when nothing else is registered.</summary>
    [Fact]
    public void The_sdk_registers_it_when_no_other_window_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSdk();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NullModuleSpaAssets>(provider.GetRequiredService<IModuleSpaAssets>());
    }

    /// <summary>A window registered before the Sdk's registration is the one the container hands out.</summary>
    [Fact]
    public void A_window_registered_first_is_not_replaced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleSpaAssets, ReplacementAssets>();
        services.AddSdk();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ReplacementAssets>(provider.GetRequiredService<IModuleSpaAssets>());
    }

    /// <summary>A window that stands in for the closed loader's.</summary>
    private sealed class ReplacementAssets : IModuleSpaAssets
    {
        /// <inheritdoc />
        public Task<ModuleSpaDescription?> DescribeAsync(string moduleId, CancellationToken ct)
        {
            return Task.FromResult<ModuleSpaDescription?>(null);
        }

        /// <inheritdoc />
        public Task<ModuleSpaAsset?> OpenAsync(string moduleId, string version, string relativePath, CancellationToken ct)
        {
            return Task.FromResult<ModuleSpaAsset?>(null);
        }
    }
}
