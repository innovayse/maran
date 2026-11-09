using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Modules;

/// <summary>
/// The loader of the open panel loads nothing: it refuses every change with the same coded,
/// unavailable failure, never touches the package it is handed, lists no module, and gives way to a
/// loader registered before it.
/// </summary>
public sealed class NullModuleLoaderTests
{
    /// <summary>The code the refusal carries.</summary>
    private const string ExpectedCode = "MarketplaceNotAvailable";

    /// <summary>Installing is refused as unavailable and the package is never opened.</summary>
    [Fact]
    public async Task Installing_is_refused_as_unavailable_without_opening_the_package()
    {
        var source = new CountingSource();

        var result = await new NullModuleLoader().InstallAsync(source, CancellationToken.None);

        AssertRefused(result.IsSuccess, result.Error);
        Assert.Equal(0, source.Opened);
    }

    /// <summary>Upgrading is refused as unavailable and the package is never opened.</summary>
    [Fact]
    public async Task Upgrading_is_refused_as_unavailable_without_opening_the_package()
    {
        var source = new CountingSource();

        var result = await new NullModuleLoader().UpgradeAsync(source, CancellationToken.None);

        AssertRefused(result.IsSuccess, result.Error);
        Assert.Equal(0, source.Opened);
    }

    /// <summary>Rolling a module back is refused as unavailable.</summary>
    [Fact]
    public async Task Rolling_back_is_refused_as_unavailable()
    {
        var result = await new NullModuleLoader().RollbackAsync("hello", CancellationToken.None);

        AssertRefused(result.IsSuccess, result.Error);
    }

    /// <summary>Unloading a module is refused as unavailable.</summary>
    [Fact]
    public async Task Unloading_is_refused_as_unavailable()
    {
        var result = await new NullModuleLoader().UnloadAsync("hello", CancellationToken.None);

        AssertRefused(result.IsSuccess, result.Error);
    }

    /// <summary>The loader holds no module, so listing answers an empty list rather than failing.</summary>
    [Fact]
    public async Task Listing_answers_an_empty_list()
    {
        var modules = await new NullModuleLoader().ListAsync(CancellationToken.None);

        Assert.Empty(modules);
    }

    /// <summary>A missing package source is a programming error, not a refusal.</summary>
    [Fact]
    public async Task A_missing_package_source_throws()
    {
        var loader = new NullModuleLoader();

        await Assert.ThrowsAsync<ArgumentNullException>(() => { return loader.InstallAsync(null!, CancellationToken.None); });
        await Assert.ThrowsAsync<ArgumentNullException>(() => { return loader.UpgradeAsync(null!, CancellationToken.None); });
    }

    /// <summary>A blank module id is a programming error, not a refusal.</summary>
    [Fact]
    public async Task A_blank_module_id_throws()
    {
        var loader = new NullModuleLoader();

        await Assert.ThrowsAsync<ArgumentException>(() => { return loader.RollbackAsync(" ", CancellationToken.None); });
        await Assert.ThrowsAsync<ArgumentException>(() => { return loader.UnloadAsync(" ", CancellationToken.None); });
    }

    /// <summary>With nothing registered before it, the Sdk's registration makes it the panel's loader.</summary>
    [Fact]
    public void Registration_makes_it_the_loader_when_none_is_registered()
    {
        var services = new ServiceCollection();

        services.AddSdk();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<NullModuleLoader>(provider.GetRequiredService<IModuleLoader>());
    }

    /// <summary>
    /// A loader registered before the Sdk's registration is the one the panel uses; the control for the
    /// test above, which a registration that always won would also pass.
    /// </summary>
    [Fact]
    public void A_loader_registered_first_is_not_replaced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleLoader, OtherLoader>();

        services.AddSdk();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<OtherLoader>(provider.GetRequiredService<IModuleLoader>());
    }

    /// <summary>Asserts a refusal that carries the seam's code and the unavailable kind.</summary>
    /// <param name="isSuccess">Whether the result succeeded.</param>
    /// <param name="error">The result's error.</param>
    private static void AssertRefused(bool isSuccess, Error? error)
    {
        Assert.False(isSuccess);
        Assert.NotNull(error);
        Assert.Equal(ExpectedCode, error.Code);
        Assert.Equal(ErrorType.Unavailable, error.Type);
    }

    /// <summary>A package source that counts how often it was opened.</summary>
    private sealed class CountingSource : IModulePackageSource
    {
        /// <inheritdoc />
        public string Origin
        {
            get { return "memory"; }
        }

        /// <summary>How many times <see cref="OpenAsync"/> was called.</summary>
        public int Opened { get; private set; }

        /// <inheritdoc />
        public Task<Stream> OpenAsync(CancellationToken ct)
        {
            Opened++;
            return Task.FromResult<Stream>(new MemoryStream());
        }
    }

    /// <summary>A stand-in for a real loader supplied by a package.</summary>
    private sealed class OtherLoader : IModuleLoader
    {
        /// <inheritdoc />
        public Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }
}
