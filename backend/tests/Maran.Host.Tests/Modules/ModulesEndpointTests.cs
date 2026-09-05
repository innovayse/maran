using System.Net;
using System.Text.Json;
using Maran.Host.Modules;

namespace Maran.Host.Tests.Modules;

/// <summary>Behavioral contract of <c>GET /api/v1/modules</c> (<see cref="Host.Modules.ModulesEndpoint"/>).</summary>
public sealed class ModulesEndpointTests : IClassFixture<PanelTestFactory>
{
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    public ModulesEndpointTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Module catalogue returns 200 with a json array shape.</summary>
    [Fact]
    public async Task Module_catalogue_returns_200_with_a_json_array_shape()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }

    /// <summary>Every compiled in module, one theory row each, read off the registry itself.</summary>
    /// <remarks>
    /// Derived rather than written out, for the reason every other fixture here asserts its own
    /// completeness: a hand-written list of module names goes stale the first time a module is added,
    /// and it goes stale SILENTLY — the theory keeps passing on the rows it still has, while the new
    /// module's manifest, tier and display-name translation are covered by nothing. It went stale
    /// three times in one plan, once per module added.
    /// </remarks>
    /// <returns>The module ids the registry contributes.</returns>
    public static TheoryData<string> CompiledInModules()
    {
        var rows = new TheoryData<string>();
        foreach (var module in ModuleRegistry.All)
        {
            rows.Add(module.Manifest.Id);
        }

        return rows;
    }

    /// <summary>Module catalogue lists every compiled in module in the registrys own load order.</summary>
    [Fact]
    public async Task Module_catalogue_lists_every_compiled_in_module_in_the_registrys_own_load_order()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(m =>
        {
            // Never null: the endpoint's own contract is that every module publishes a name, and a
            // null here would be a defect this test should report as a mismatch rather than skip.
            return m.GetProperty("name").GetString()!;
        }).ToList();

        // Load order is what the registry promises, and the endpoint's contract is to report it
        // unchanged — including a module compiled in later, which a hard-coded list would have made
        // this test forbid rather than describe.
        Assert.Equal(
            ModuleRegistry.All.Select(module =>
            {
                return module.Manifest.Id;
            }).ToList(),
            names);

        // Identity owning sign-in is why it leads: every other module's endpoints are meaningless
        // until its services are registered. That is the one position the order actually promises.
        Assert.Equal("identity", names[0]);
    }

    /// <summary>Every compiled in module publishes a tier a state and a translated display name.</summary>
    [Theory]
    [MemberData(nameof(CompiledInModules))]
    public async Task Every_compiled_in_module_publishes_a_tier_a_state_and_a_translated_display_name(string name)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var module = body.RootElement.EnumerateArray().Single(m =>
        {
            return m.GetProperty("name").GetString() == name;
        });
        Assert.Equal("included", module.GetProperty("tier").GetString());
        Assert.True(module.GetProperty("isEnabled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(module.GetProperty("displayName").GetString()));
    }
}
