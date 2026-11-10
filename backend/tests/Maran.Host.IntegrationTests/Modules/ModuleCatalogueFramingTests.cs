using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// What <c>GET /api/v1/modules</c> says about a packaged module's screen against a real host: its version,
/// state, origin and frame, and that a module which shipped with the panel, is not active, holds a bundle of
/// another version, or is not composed at all is not offered a frame.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the localized failure sentence of a degraded module; the fake loader's cause is a code no
/// table claims, which is what this class pins (no machine code is shown). The resolved sentence is observed
/// by the hermetic catalogue tests in the Host unit project.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ModuleCatalogueFramingTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ModuleCatalogueFramingTests(PostgresFixture postgres)
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

    /// <summary>An active packaged module carries its version, state, origin and a frame the Host built.</summary>
    [Fact]
    public async Task An_active_packaged_module_carries_its_version_lifecycle_origin_and_frame()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);

        using var catalogue = await ReadAsync(world);
        var module = ModuleOf(catalogue, FramedModuleWorld.ModuleId);

        Assert.Equal("1.0.0", module.GetProperty("version").GetString());
        Assert.Equal("active", module.GetProperty("lifecycle").GetString());
        Assert.True(module.GetProperty("isPackaged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("failureDisplay").ValueKind);

        var spa = module.GetProperty("spa");
        Assert.Equal("/module-frames/hello/1.0.0/", spa.GetProperty("frameUrl").GetString());
        Assert.Equal(world.Integrity, spa.GetProperty("integrity").GetString());
        Assert.Equal(
            ["en", "ru"],
            spa.GetProperty("locales").EnumerateArray().Select(locale => { return locale.GetString()!; }).ToArray());
    }

    /// <summary>A module that shipped with the panel has a version and a state, is not packaged and has no frame.</summary>
    [Fact]
    public async Task A_shipped_module_has_no_frame_and_is_not_packaged()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);

        using var catalogue = await ReadAsync(world);
        var module = ModuleOf(catalogue, "identity");

        Assert.False(string.IsNullOrWhiteSpace(module.GetProperty("version").GetString()));
        Assert.Equal("active", module.GetProperty("lifecycle").GetString());
        Assert.False(module.GetProperty("isPackaged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("spa").ValueKind);
        Assert.Equal(JsonValueKind.Null, module.GetProperty("failureDisplay").ValueKind);
    }

    /// <summary>A degraded packaged module is listed degraded with no frame, and no machine code stands in for a sentence.</summary>
    [Fact]
    public async Task A_degraded_packaged_module_is_listed_degraded_without_a_frame()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        await world.DegradeAsync();

        using var catalogue = await ReadAsync(world);
        var module = ModuleOf(catalogue, FramedModuleWorld.ModuleId);

        Assert.Equal("degraded", module.GetProperty("lifecycle").GetString());
        Assert.True(module.GetProperty("isPackaged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("spa").ValueKind);
        Assert.NotEqual(FakeModuleLoader.LeaseLapsed, module.GetProperty("failureDisplay").GetString());
    }

    /// <summary>A bundle built for another version than the running one is not offered.</summary>
    [Fact]
    public async Task A_bundle_of_another_version_is_not_offered()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        world.Assets.Describe(
            FramedModuleWorld.ModuleId,
            new ModuleSpaDescription("0.9.0", FramedModuleWorld.Entry, world.Integrity, ["en"]));

        using var catalogue = await ReadAsync(world);
        var module = ModuleOf(catalogue, FramedModuleWorld.ModuleId);

        Assert.Equal("active", module.GetProperty("lifecycle").GetString());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("spa").ValueKind);
    }

    /// <summary>A module that is installed but not yet composed stays absent until the next start.</summary>
    [Fact]
    public async Task A_module_installed_but_not_composed_stays_absent()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        world.Lease.Entitle("later");
        var installed = await world.Loader.InstallAsync(
            new InMemoryModulePackageSource(FakePackage.Sealed("later", "1.0.0").ToBytes()), CancellationToken.None);
        Assert.True(installed.IsSuccess);

        using var catalogue = await ReadAsync(world);
        var names = catalogue.RootElement.EnumerateArray()
            .Select(module => { return module.GetProperty("name").GetString(); })
            .ToList();

        Assert.DoesNotContain("later", names);
        Assert.Contains(FramedModuleWorld.ModuleId, names);
    }

    /// <summary>Reads the catalogue as an anonymous caller.</summary>
    /// <param name="world">The booted host.</param>
    /// <returns>The parsed document; the caller disposes it.</returns>
    private static async Task<JsonDocument> ReadAsync(FramedModuleWorld world)
    {
        using var client = world.Factory.CreateClient();
        var response = await client.GetAsync("/api/v1/modules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Finds one module in a catalogue document.</summary>
    /// <param name="catalogue">The catalogue.</param>
    /// <param name="id">The module's name.</param>
    /// <returns>The module's element.</returns>
    private static JsonElement ModuleOf(JsonDocument catalogue, string id)
    {
        return catalogue.RootElement.EnumerateArray()
            .Single(module => { return module.GetProperty("name").GetString() == id; });
    }
}
