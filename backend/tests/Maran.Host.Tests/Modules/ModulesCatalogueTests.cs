using System.Text.Json;
using Maran.Host.Modules;
using Maran.Host.Tests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// What <c>GET /api/v1/modules</c> now carries beyond identity: the minimum panel version, the kind,
/// the permissions, and the module's menu and routes localized in the request's culture and cut to
/// the caller's audience.
/// </summary>
/// <remarks>
/// Expectations are derived from the registry's own manifests. Each localization check is paired
/// with a census guard (some module really contributes a menu entry) because a catalogue that sent
/// nothing would satisfy every "each entry is localized" loop over an empty list.
/// </remarks>
public sealed class ModulesCatalogueTests : IClassFixture<PanelTestFactory>
{
    /// <summary>The booted host factory the per-caller hosts derive from.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    /// <param name="factory">The hermetic host factory.</param>
    public ModulesCatalogueTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Every module states its minimum panel version, kind and permissions beside its identity.</summary>
    [Fact]
    public async Task Every_module_states_its_minimum_panel_version_its_kind_and_its_permissions()
    {
        using var body = await ReadAsync(new AdministratorCaller(Guid.NewGuid(), "root"), "en");

        var modules = body.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(modules);
        foreach (var module in modules)
        {
            var id = module.GetProperty("name").GetString()!;
            var manifest = ModuleRegistry.All.Single(candidate => { return candidate.Manifest.Id == id; }).Manifest;

            Assert.Equal(manifest.MinPanelVersion, module.GetProperty("minPanelVersion").GetString());
            Assert.Equal(manifest.Kind.ToString(), module.GetProperty("kind").GetString(), ignoreCase: true);
            Assert.Equal(manifest.Permissions.Count, module.GetProperty("permissions").GetArrayLength());
        }
    }

    /// <summary>An administrator receives every menu entry of every module, each label resolved and none left as its own key.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task An_administrator_receives_every_menu_entry_with_its_label_resolved(string culture)
    {
        using var body = await ReadAsync(new AdministratorCaller(Guid.NewGuid(), "root"), culture);

        var declared = ModuleRegistry.All.SelectMany(module => { return module.Manifest.Menu; }).ToList();
        Assert.NotEmpty(declared);

        var sent = body.RootElement.EnumerateArray()
            .SelectMany(module => { return module.GetProperty("menu").EnumerateArray(); })
            .ToList();
        Assert.Equal(
            declared.Select(item => { return item.Key; }).Order(StringComparer.Ordinal),
            sent.Select(item => { return item.GetProperty("key").GetString()!; }).Order(StringComparer.Ordinal));

        foreach (var item in sent)
        {
            var key = item.GetProperty("key").GetString()!;
            var labelKey = declared.Single(candidate => { return candidate.Key == key; }).LabelKey;
            var label = item.GetProperty("label").GetString();

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(labelKey, label);
        }
    }

    /// <summary>The labels differ between English and Russian, so the text really is resolved per culture.</summary>
    [Fact]
    public async Task Menu_labels_are_resolved_in_the_request_culture()
    {
        var administrator = new AdministratorCaller(Guid.NewGuid(), "root");
        using var english = await ReadAsync(administrator, "en");
        using var russian = await ReadAsync(administrator, "ru");

        var englishLabels = LabelsByKey(english);
        var russianLabels = LabelsByKey(russian);

        Assert.NotEmpty(englishLabels);
        Assert.Contains(englishLabels, pair => { return russianLabels[pair.Key] != pair.Value; });
    }

    /// <summary>A customer receives exactly the entries addressed to everyone, and the administrator has more (inverse control).</summary>
    [Fact]
    public async Task A_customer_receives_only_the_menu_entries_addressed_to_everyone()
    {
        using var customer = await ReadAsync(new CustomerCaller(Guid.NewGuid(), "acme", Guid.NewGuid()), "en");
        using var administrator = await ReadAsync(new AdministratorCaller(Guid.NewGuid(), "root"), "en");

        var expected = ModuleRegistry.All
            .Where(module => { return module.Manifest.Audience == ModuleAudience.Everyone; })
            .SelectMany(module => { return module.Manifest.Menu; })
            .Where(item => { return item.Audience == ModuleAudience.Everyone; })
            .Select(item => { return item.Key; })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, LabelsByKey(customer).Keys.Order(StringComparer.Ordinal));
        Assert.True(LabelsByKey(administrator).Count > expected.Count);
    }

    /// <summary>Every route is sent with a title that is resolved text, not the resource key.</summary>
    [Fact]
    public async Task Every_route_is_sent_with_a_resolved_title()
    {
        using var body = await ReadAsync(new AdministratorCaller(Guid.NewGuid(), "root"), "en");

        var declared = ModuleRegistry.All.SelectMany(module => { return module.Manifest.Routes; }).ToList();
        Assert.NotEmpty(declared);

        var sent = body.RootElement.EnumerateArray()
            .SelectMany(module => { return module.GetProperty("routes").EnumerateArray(); })
            .ToList();
        Assert.Equal(declared.Count, sent.Count);
        foreach (var route in sent)
        {
            var name = route.GetProperty("name").GetString()!;
            var titleKey = declared.Single(candidate => { return candidate.Name == name; }).TitleKey;
            var title = route.GetProperty("title").GetString();

            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.NotEqual(titleKey, title);
        }
    }

    /// <summary>Reads the catalogue as a caller in a culture.</summary>
    /// <param name="caller">The caller the host reports for the request.</param>
    /// <param name="culture">The <c>Accept-Language</c> value.</param>
    /// <returns>The parsed document; the caller disposes it.</returns>
    private async Task<JsonDocument> ReadAsync(Caller caller, string culture)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICurrentUser>(_ => { return new FixedCurrentUser(caller); });
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", culture);

        var response = await client.GetAsync("/api/v1/modules");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Maps each menu entry key in a catalogue document to its label.</summary>
    /// <param name="document">The catalogue.</param>
    /// <returns>The labels by entry key.</returns>
    private static Dictionary<string, string> LabelsByKey(JsonDocument document)
    {
        return document.RootElement.EnumerateArray()
            .SelectMany(module => { return module.GetProperty("menu").EnumerateArray(); })
            .ToDictionary(
                item => { return item.GetProperty("key").GetString()!; },
                item => { return item.GetProperty("label").GetString()!; },
                StringComparer.Ordinal);
    }
}
