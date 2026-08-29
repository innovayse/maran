using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.Tests.Modules;

/// <summary>Behavioral contract of <c>GET /api/v1/modules</c> (<see cref="Host.Modules.ModulesEndpoint"/>).</summary>
public sealed class ModulesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    public ModulesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(PanelTestSettings.EncryptionKeyPath, PanelTestSettings.EncryptionKey));
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
    public async Task Module_catalogue_lists_the_accounts_module_as_the_first_compiled_in_module()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetArrayLength());

        var accounts = body.RootElement[0];
        Assert.Equal("accounts", accounts.GetProperty("name").GetString());
        Assert.Equal("included", accounts.GetProperty("tier").GetString());
        Assert.True(accounts.GetProperty("isEnabled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(accounts.GetProperty("displayName").GetString()));
    }
}
