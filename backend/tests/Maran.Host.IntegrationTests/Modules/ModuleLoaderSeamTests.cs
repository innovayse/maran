using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.Modules;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// The module-loader seam against a real host and a real PostgreSQL, driven by <see cref="FakeModuleLoader"/>:
/// what a loader does and refuses is recorded in the audit journal, a refusal leaves the previous state
/// intact, an install asks for a restart instead of activating in place, and a lapsed lease degrades only
/// the module it stopped covering.
/// </summary>
/// <remarks>
/// <para>
/// The behaviours asserted here are the loader contract. The closed loader's own suite repeats them against
/// its real package format; they are named for what the panel must do, not for the fake.
/// </para>
/// <para>
/// UNOBSERVED HERE: that a loaded module's routes answer, its tables are created and its handlers run. A
/// module joins the panel at the next start, which one running host cannot cross, and the open build has no
/// package format to load. What is observed is everything the Host owns around that moment.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ModuleLoaderSeamTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ModuleLoaderSeamTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A sealed package is held, audited, and a restart is asked for.</summary>
    [Fact]
    public async Task A_sealed_package_is_held_audited_and_a_restart_is_asked_for()
    {
        await using var world = await World.BootAsync(_pg, "hello");

        var installed = await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);

        Assert.True(installed.IsSuccess);
        var held = Assert.Single(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal(("hello", "1.0.0", ModuleLifecycleState.Installed), (held.ModuleId, held.Version, held.State));
        Assert.Equal(["hello"], world.Restart.Requests);

        var entry = Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleInstalled));
        Assert.Equal("module=hello;version=1.0.0", entry.Subject);
        Assert.Equal("maran-marketplace", entry.ActorUsername);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An unsealed package is refused, audited as a failure, and nothing is held.</summary>
    [Fact]
    public async Task An_unsealed_package_is_refused_audited_and_nothing_is_held()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        var unsealed = FakePackage.Sealed("hello", "1.0.0") with { Seal = string.Empty };

        await AssertRefusedAsync(world, unsealed.ToBytes(), FakeModuleLoader.SealInvalid, "module=hello;version=1.0.0");
    }

    /// <summary>A package sealed by another key is refused.</summary>
    [Fact]
    public async Task A_package_sealed_by_another_key_is_refused()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        var foreign = FakePackage.Sealed("hello", "1.0.0", FakePackage.OtherKey);

        await AssertRefusedAsync(world, foreign.ToBytes(), FakeModuleLoader.SealInvalid, "module=hello;version=1.0.0");
    }

    /// <summary>A package edited after it was sealed is refused.</summary>
    [Fact]
    public async Task A_package_edited_after_it_was_sealed_is_refused()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        var tampered = FakePackage.Sealed("hello", "1.0.0") with { Body = "payload, edited" };

        await AssertRefusedAsync(world, tampered.ToBytes(), FakeModuleLoader.SealInvalid, "module=hello;version=1.0.0");
    }

    /// <summary>Bytes that are not a package are refused, and the refusal names no module.</summary>
    [Fact]
    public async Task Bytes_that_are_not_a_package_are_refused_and_the_refusal_names_no_module()
    {
        await using var world = await World.BootAsync(_pg, "hello");

        await AssertRefusedAsync(world, "not a package"u8.ToArray(), FakeModuleLoader.Unreadable, null);
    }

    /// <summary>A package for a module the lease does not cover is refused.</summary>
    [Fact]
    public async Task A_package_for_a_module_the_lease_does_not_cover_is_refused()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        var uncovered = FakePackage.Sealed("other", "1.0.0");

        await AssertRefusedAsync(world, uncovered.ToBytes(), FakeModuleLoader.NotEntitled, "module=other;version=1.0.0");
    }

    /// <summary>A refused upgrade leaves the installed version, the restart count and the registry as they were.</summary>
    [Fact]
    public async Task A_refused_upgrade_leaves_the_installed_module_untouched()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);
        var before = await world.Loader.ListAsync(CancellationToken.None);

        var tampered = FakePackage.Sealed("hello", "2.0.0") with { Body = "edited" };
        var refused = await world.Loader.UpgradeAsync(new InMemoryModulePackageSource(tampered.ToBytes()), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(before, await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal(["hello"], world.Restart.Requests);
        Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleRefused));
    }

    /// <summary>An upgrade keeps the earlier version for a rollback, and a downgrade is refused.</summary>
    [Fact]
    public async Task An_upgrade_keeps_the_earlier_version_for_a_rollback_and_a_downgrade_is_refused()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);

        var upgraded = await world.Loader.UpgradeAsync(World.Source(FakePackage.Sealed("hello", "1.1.0")), CancellationToken.None);
        var downgraded = await world.Loader.UpgradeAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);
        Assert.True(upgraded.IsSuccess);
        Assert.Equal(FakeModuleLoader.Downgrade, downgraded.Error!.Code);
        Assert.Equal("1.1.0", Assert.Single(await world.Loader.ListAsync(CancellationToken.None)).Version);

        var rolledBack = await world.Loader.RollbackAsync("hello", CancellationToken.None);
        var again = await world.Loader.RollbackAsync("hello", CancellationToken.None);

        Assert.True(rolledBack.IsSuccess);
        Assert.Equal("1.0.0", Assert.Single(await world.Loader.ListAsync(CancellationToken.None)).Version);
        Assert.Equal(FakeModuleLoader.NothingToRollBack, again.Error!.Code);
        Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleUpgraded));
        Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleRolledBack));
    }

    /// <summary>Unloading removes the module, audits it, and asks for a restart.</summary>
    [Fact]
    public async Task Unloading_removes_the_module_audits_it_and_asks_for_a_restart()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);

        var unloaded = await world.Loader.UnloadAsync("hello", CancellationToken.None);

        Assert.True(unloaded.IsSuccess);
        Assert.Empty(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal(["hello", "hello"], world.Restart.Requests);
        Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleUnloaded));
    }

    /// <summary>A lapsed lease degrades the paid module, audits it once, and the panel still serves its core.</summary>
    [Fact]
    public async Task A_lapsed_lease_degrades_only_the_paid_module_and_the_core_still_serves()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);
        var catalogueBefore = await world.CatalogueNamesAsync();

        world.Lease.Lapse();
        await world.Loader.ReconcileLeaseAsync(CancellationToken.None);
        await world.Loader.ReconcileLeaseAsync(CancellationToken.None);

        var held = Assert.Single(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal(ModuleLifecycleState.Degraded, held.State);
        Assert.Equal(FakeModuleLoader.LeaseLapsed, held.Failure!.Code);
        Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleDegraded));
        Assert.Equal(catalogueBefore, await world.CatalogueNamesAsync());

        var registry = world.Factory.Services.GetRequiredService<IModuleRegistry>();
        var composed = await registry.ListAsync(CancellationToken.None);
        Assert.NotEmpty(composed);
        Assert.All(composed, module => { Assert.Equal(ModuleLifecycleState.Active, module.State); });
    }

    /// <summary>With a valid lease the held module is not degraded and nothing is audited as a lapse (inverse control).</summary>
    [Fact]
    public async Task With_a_valid_lease_the_held_module_is_not_degraded()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);

        await world.Loader.ReconcileLeaseAsync(CancellationToken.None);

        Assert.Equal(ModuleLifecycleState.Installed, Assert.Single(await world.Loader.ListAsync(CancellationToken.None)).State);
        Assert.Empty(await world.AuditAsync(MarketplaceAuditActions.ModuleDegraded));
    }

    /// <summary>A package waiting for the next start is not offered by the catalogue.</summary>
    [Fact]
    public async Task A_package_waiting_for_the_next_start_is_not_offered_by_the_catalogue()
    {
        await using var world = await World.BootAsync(_pg, "hello");
        var before = await world.CatalogueNamesAsync();

        await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);

        var after = await world.CatalogueNamesAsync();
        Assert.Equal(before, after);
        Assert.DoesNotContain("hello", after);
    }

    /// <summary>The open build registers the null loader and lists every shipped module as active.</summary>
    [Fact]
    public async Task The_open_build_registers_the_null_loader_and_lists_every_shipped_module_as_active()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);

        Assert.IsType<NullModuleLoader>(factory.Services.GetRequiredService<IModuleLoader>());

        var listed = await factory.Services.GetRequiredService<IModuleRegistry>().ListAsync(CancellationToken.None);

        Assert.Equal(ModuleRegistry.All.Count, listed.Count);
        Assert.All(listed, module => { Assert.Equal(ModuleLifecycleState.Active, module.State); });
    }

    /// <summary>Offers a package that must be refused and asserts the refusal, its audit entry, and the unchanged state.</summary>
    /// <param name="world">The booted host and its loader.</param>
    /// <param name="bytes">The package bytes offered.</param>
    /// <param name="expectedCode">The code the refusal must carry.</param>
    /// <param name="expectedModulePart">
    /// The module and version the audit subject must name, or null when the package named none because it
    /// could not be read.
    /// </param>
    private static async Task AssertRefusedAsync(World world, byte[] bytes, string expectedCode, string? expectedModulePart)
    {
        // The accepting control comes first and stays installed, so "nothing changed" is compared
        // against a state that is not empty.
        var accepted = await world.Loader.InstallAsync(World.Source(FakePackage.Sealed("hello", "1.0.0")), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
        var before = await world.Loader.ListAsync(CancellationToken.None);
        var restartsBefore = world.Restart.Requests.Count;

        var refused = await world.Loader.InstallAsync(new InMemoryModulePackageSource(bytes), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(expectedCode, refused.Error!.Code);
        Assert.Equal(before, await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal(restartsBefore, world.Restart.Requests.Count);

        var entry = Assert.Single(await world.AuditAsync(MarketplaceAuditActions.ModuleRefused));
        Assert.False(entry.Succeeded);
        var reason = "reason=" + expectedCode;
        Assert.Equal(expectedModulePart is null ? reason : expectedModulePart + ";" + reason, entry.Subject);
    }

    /// <summary>The booted host, the fake loader and the stand-ins around it.</summary>
    private sealed class World : IAsyncDisposable
    {
        /// <summary>The booted host.</summary>
        public WebApplicationFactory<Program> Factory { get; }

        /// <summary>The loader under test.</summary>
        public FakeModuleLoader Loader { get; }

        /// <summary>The lease the loader consults.</summary>
        public FakeLicenceLease Lease { get; }

        /// <summary>The restart stand-in.</summary>
        public RecordingPanelRestart Restart { get; }

        /// <summary>Creates the world.</summary>
        /// <param name="factory">The booted host.</param>
        /// <param name="loader">The loader under test.</param>
        /// <param name="lease">The lease the loader consults.</param>
        /// <param name="restart">The restart stand-in.</param>
        private World(
            WebApplicationFactory<Program> factory,
            FakeModuleLoader loader,
            FakeLicenceLease lease,
            RecordingPanelRestart restart)
        {
            Factory = factory;
            Loader = loader;
            Lease = lease;
            Restart = restart;
        }

        /// <summary>Boots a host whose loader is the fake one, with the given modules covered by the lease.</summary>
        /// <param name="database">The test's own database.</param>
        /// <param name="entitled">The ids of the modules the lease covers.</param>
        /// <returns>The world, which the caller disposes.</returns>
        public static async Task<World> BootAsync(TestDatabase database, params string[] entitled)
        {
            var lease = new FakeLicenceLease();
            foreach (var id in entitled)
            {
                lease.Entitle(id);
            }

            var restart = new RecordingPanelRestart();
            var factory = EndpointHarness.CreateFactory(database, services =>
            {
                services.AddSingleton<ILicenceLease>(lease);
                services.AddSingleton<IPanelRestart>(restart);
                services.AddSingleton<IModuleLoader>(provider =>
                {
                    return new FakeModuleLoader(
                        provider.GetRequiredService<IServiceScopeFactory>(), lease, restart);
                });
            });
            await EndpointHarness.MigrateAsync(factory);

            return new World(factory, (FakeModuleLoader)factory.Services.GetRequiredService<IModuleLoader>(), lease, restart);
        }

        /// <summary>Wraps a package as a source.</summary>
        /// <param name="package">The package.</param>
        /// <returns>The source.</returns>
        public static InMemoryModulePackageSource Source(FakePackage package)
        {
            return new InMemoryModulePackageSource(package.ToBytes());
        }

        /// <summary>Reads the audit journal's entries of one action, straight from the table.</summary>
        /// <param name="action">The action to read.</param>
        /// <returns>The entries, oldest first.</returns>
        public async Task<List<AuditEvent>> AuditAsync(string action)
        {
            using var scope = Factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // The test reads the table whole: no caller is signed in, and what is asserted is the journal.
            return await identity.AuditEvents.IgnoreQueryFilters()
                .Where(entry => entry.Action == action)
                .OrderBy(entry => entry.OccurredAt)
                .ToListAsync();
        }

        /// <summary>Reads the names of the modules the catalogue offers an anonymous caller.</summary>
        /// <returns>The module names, in the order the catalogue lists them.</returns>
        public async Task<List<string>> CatalogueNamesAsync()
        {
            using var client = Factory.CreateClient();
            var response = await client.GetAsync("/api/v1/modules");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.EnumerateArray()
                .Select(module => { return module.GetProperty("name").GetString()!; })
                .ToList();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
        }
    }
}
