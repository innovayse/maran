using System.Net;
using System.Text.Json;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;

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

    /// <summary>
    /// Every module addressed to <see cref="ModuleAudience.Everyone"/>, one theory row each, read
    /// off the registry itself.
    /// </summary>
    /// <remarks>
    /// Derived rather than written out, for the reason every other fixture here asserts its own
    /// completeness: a hand-written list of module names goes stale the first time a module is added,
    /// and it goes stale SILENTLY — the theory keeps passing on the rows it still has, while the new
    /// module's manifest, tier and display-name translation are covered by nothing. It went stale
    /// three times in one plan, once per module added.
    ///
    /// Filtered to <see cref="ModuleAudience.Everyone"/> because every test using this data drives
    /// an anonymous client, and the catalogue now answers an anonymous caller — who reads as a
    /// non-administrator — with only that subset (<see cref="ModulesEndpoint"/>). An
    /// administrator-only module is covered separately, by the test asserting the catalogue hides
    /// it from a non-administrator, not by this list.
    /// </remarks>
    /// <returns>The ids of the registry's customer-facing modules.</returns>
    public static TheoryData<string> EveryoneModules()
    {
        var rows = new TheoryData<string>();
        foreach (var module in ModuleRegistry.All)
        {
            if (module.Manifest.Audience == ModuleAudience.Everyone)
            {
                rows.Add(module.Manifest.Id);
            }
        }

        return rows;
    }

    /// <summary>Module catalogue lists every everyone-audience module in the registrys own load order.</summary>
    [Fact]
    public async Task Module_catalogue_lists_every_everyone_audience_module_in_the_registrys_own_load_order()
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
        // unchanged for the modules it is allowed to show this (anonymous, non-administrator)
        // caller — including one compiled in later, which a hard-coded list would have made this
        // test forbid rather than describe.
        Assert.Equal(
            ModuleRegistry.All
                .Where(module =>
                {
                    return module.Manifest.Audience == ModuleAudience.Everyone;
                })
                .Select(module =>
                {
                    return module.Manifest.Id;
                })
                .ToList(),
            names);

        // Identity owning sign-in is why it leads: every other module's endpoints are meaningless
        // until its services are registered. That is the one position the order actually promises.
        Assert.Equal("identity", names[0]);
    }

    /// <summary>
    /// The catalogue hides administrator-only modules from a caller who is not an administrator.
    /// </summary>
    /// <remarks>
    /// This client is anonymous, and <see cref="Maran.SharedKernel.Interfaces.ICurrentUser.IsAdmin"/>
    /// reads false for one — the same answer a signed-in customer gets. Presentation only: an
    /// administrator-only module missing here is not thereby protected, its own endpoints are
    /// (<see cref="ModulesEndpoint"/>).
    /// </remarks>
    [Fact]
    public async Task The_catalogue_hides_administrator_only_modules_from_a_caller_who_is_not_an_administrator()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(m =>
        {
            return m.GetProperty("name").GetString();
        }).ToList();

        var administratorOnlyModules = ModuleRegistry.All
            .Where(module =>
            {
                return module.Manifest.Audience == ModuleAudience.AdministratorOnly;
            })
            .Select(module =>
            {
                return module.Manifest.Id;
            })
            .ToList();

        // A registry with no administrator-only module at all would make this assertion pass
        // vacuously, so require there to be at least one — there are five today (Accounts,
        // Firewall, Licensing, Monitoring, Notifications).
        Assert.NotEmpty(administratorOnlyModules);
        foreach (var moduleId in administratorOnlyModules)
        {
            Assert.DoesNotContain(moduleId, names);
        }
    }

    /// <summary>Every everyone-audience module publishes a tier a state and a translated display name.</summary>
    [Theory]
    [MemberData(nameof(EveryoneModules))]
    public async Task Every_everyone_audience_module_publishes_a_tier_a_state_and_a_translated_display_name(string name)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var module = body.RootElement.EnumerateArray().Single(m =>
        {
            return m.GetProperty("name").GetString() == name;
        });
        Assert.Equal("included", module.GetProperty("tier").GetString());
        Assert.Equal("Included", module.GetProperty("tierDisplayName").GetString());
        Assert.True(module.GetProperty("isEnabled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(module.GetProperty("displayName").GetString()));
    }

    /// <summary>The catalogue names the licence tier in the callers language beside the machine value.</summary>
    /// <remarks>
    /// The defect this asserts against, in full: the tier travelled as <c>addOn</c> alone, and the
    /// upgrade screen interpolated it into a Russian sentence, so an operator read
    /// "Он доступен в тарифе addOn." The assertion is on the exact Russian VALUE and in a
    /// NON-English locale, because a Russian operator reading an English constant is the whole of
    /// what was broken — and the machine value is asserted to be still on the wire beside it, since
    /// the SPA keys and branches on that and a fix that replaced it would break the panel instead.
    /// </remarks>
    [Fact]
    public async Task Module_catalogue_names_the_licence_tier_in_russian_beside_the_machine_value()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "ru");

        var response = await client.GetAsync("/api/v1/modules");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var module = body.RootElement.EnumerateArray().First();
        Assert.Equal("included", module.GetProperty("tier").GetString());
        Assert.Equal("Входит в поставку", module.GetProperty("tierDisplayName").GetString());
    }
}
