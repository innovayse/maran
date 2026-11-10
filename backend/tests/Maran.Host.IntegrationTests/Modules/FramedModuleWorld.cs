using System.Security.Cryptography;
using System.Text;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A host booted for the frame tests: the shipped modules plus one packaged module composed into it, a
/// loader that reports the state the test sets, and a screen-bundle window holding that module's files.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: that a real package is verified and composed at start. The composition is built here
/// the way the start builds it (the composer, with the shipped modules and one packaged one), and the
/// loader and the bundle window are the fakes of the contract tests.
/// </remarks>
public sealed class FramedModuleWorld : IAsyncDisposable
{
    /// <summary>The id of the packaged module composed into the host.</summary>
    public const string ModuleId = "hello";

    /// <summary>The version of the packaged module that runs.</summary>
    public const string Version = "1.0.0";

    /// <summary>The entry script's content.</summary>
    public const string EntryScript = "console.log('hello module');";

    /// <summary>The entry script's file name.</summary>
    public const string Entry = "entry.js";

    /// <summary>The booted host.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The loader the host reports module state from.</summary>
    public FakeModuleLoader Loader { get; }

    /// <summary>The lease the loader consults.</summary>
    public FakeLicenceLease Lease { get; }

    /// <summary>The screen-bundle window.</summary>
    public FakeModuleSpaAssets Assets { get; }

    /// <summary>The integrity value the bundle reports for its entry script.</summary>
    public string Integrity { get; }

    /// <summary>Creates the world.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="loader">The loader the host reports module state from.</param>
    /// <param name="lease">The lease the loader consults.</param>
    /// <param name="assets">The screen-bundle window.</param>
    /// <param name="integrity">The integrity value the bundle reports.</param>
    private FramedModuleWorld(
        WebApplicationFactory<Program> factory,
        FakeModuleLoader loader,
        FakeLicenceLease lease,
        FakeModuleSpaAssets assets,
        string integrity)
    {
        Factory = factory;
        Loader = loader;
        Lease = lease;
        Assets = assets;
        Integrity = integrity;
    }

    /// <summary>Computes the integrity value a browser checks a script against.</summary>
    /// <param name="content">The script's bytes.</param>
    /// <returns><c>sha384-</c> and the base64 digest.</returns>
    public static string IntegrityOf(byte[] content)
    {
        return "sha384-" + Convert.ToBase64String(SHA384.HashData(content));
    }

    /// <summary>
    /// Boots a host with the packaged module composed and a bundle of <see cref="Version"/> on offer: the
    /// entry script, a stylesheet and a document inside the bundle.
    /// </summary>
    /// <param name="database">The test's own database.</param>
    /// <returns>The world, which the caller disposes.</returns>
    public static async Task<FramedModuleWorld> BootAsync(TestDatabase database)
    {
        var lease = new FakeLicenceLease();
        lease.Entitle(ModuleId);

        var entry = Encoding.UTF8.GetBytes(EntryScript);
        var integrity = IntegrityOf(entry);
        var assets = new FakeModuleSpaAssets();
        assets.Describe(ModuleId, new ModuleSpaDescription(Version, Entry, integrity, ["en", "ru"]));
        assets.AddFile(ModuleId, Version, Entry, entry);
        assets.AddFile(ModuleId, Version, "assets/style.css", "body { margin: 0 }"u8.ToArray());
        assets.AddFile(ModuleId, Version, "page.html", "<html><body>mine</body></html>"u8.ToArray());

        var restart = new RecordingPanelRestart();
        var composition = PackagedModuleComposer.Compose(
            ModuleRegistry.All, [new FakePackagedModule(ModuleId, Version)], new PanelVersion());

        var factory = EndpointHarness.CreateFactory(database, services =>
        {
            services.AddSingleton<ILicenceLease>(lease);
            services.AddSingleton<IPanelRestart>(restart);
            services.AddSingleton<IModuleSpaAssets>(assets);
            services.AddSingleton(composition);
            services.AddSingleton<IModuleLoader>(provider =>
            {
                return new FakeModuleLoader(provider.GetRequiredService<IServiceScopeFactory>(), lease, restart);
            });
        });
        await EndpointHarness.MigrateAsync(factory);

        return new FramedModuleWorld(
            factory, (FakeModuleLoader)factory.Services.GetRequiredService<IModuleLoader>(), lease, assets, integrity);
    }

    /// <summary>
    /// Installs the module in the fake loader and then lapses the lease, so the loader reports it degraded
    /// while it stays composed.
    /// </summary>
    /// <returns>Resolves once the loader reports the module degraded.</returns>
    public async Task DegradeAsync()
    {
        var installed = await Loader.InstallAsync(
            new InMemoryModulePackageSource(FakePackage.Sealed(ModuleId, Version).ToBytes()), CancellationToken.None);
        Assert.True(installed.IsSuccess);

        Lease.Lapse();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}
