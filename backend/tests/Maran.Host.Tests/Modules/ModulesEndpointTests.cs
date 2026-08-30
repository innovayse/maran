using System.Net;
using System.Text.Json;

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

    [Fact]
    public async Task Module_catalogue_returns_200_with_a_json_array_shape()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }

    [Fact]
    public async Task Module_catalogue_lists_identity_first_then_accounts()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(m =>
        {
            return m.GetProperty("name").GetString();
        }).ToList();

        // Load order is what the registry promises, and Identity owning sign-in is why it leads.
        Assert.Equal(["identity", "accounts"], names);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("accounts")]
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
