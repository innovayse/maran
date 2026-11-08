using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>/api/v1/me/dashboard-layout</c> over real HTTP: a person reads the panel's defaults, saves their
/// own arrangement, reads it back with a key this panel has no default for kept in place, a second
/// person never sees it, malformed layouts are refused with their codes, and every save leaves one
/// audit entry. The endpoint is caller-scoped and not tenant-scoped (no id of another person's row
/// can be named), so there is no IDOR 404 to test; the isolation test is its equivalent.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class DashboardLayoutEndpointTests : IAsyncLifetime
{
    /// <summary>The route of the endpoint under test.</summary>
    private const string Endpoint = "/api/v1/me/dashboard-layout";

    /// <summary>The widget keys an administrator who stored nothing is offered, in order.</summary>
    private static readonly string[] AdministratorWidgets =
    {
        "attention", "firstRun", "cpu", "memory", "disk", "network", "services", "totals", "activity",
    };

    /// <summary>
    /// The widget keys after the administrator saved memory, a key of their own and the hidden cpu, in that
    /// order: the saved entries first as saved, then the defaults they did not mention, in default order.
    /// </summary>
    private static readonly string[] MemoryFirstThenUnknownThenDefaults =
    {
        "memory", "mailQueue", "cpu", "attention", "firstRun", "disk", "network", "services", "totals", "activity",
    };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DashboardLayoutEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator reads the defaults, saves an arrangement with an unknown key, reads it back and the save is audited.</summary>
    [Fact]
    public async Task An_administrator_saves_an_arrangement_and_reads_it_back_and_the_save_is_audited()
    {
        var audit = new RecordingAuditWriter();
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        Assert.Equal(AdministratorWidgets, await WidgetKeysAsync(admin));

        var save = await admin.PutAsJsonAsync(Endpoint, new
        {
            Widgets = new[]
            {
                new { Key = "memory", Hidden = false },
                new { Key = "mailQueue", Hidden = false },
                new { Key = "cpu", Hidden = true },
            },
            Actions = new[] { new { Key = "addSite", Hidden = true } },
        });

        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        using var layout = JsonDocument.Parse(await admin.GetStringAsync(Endpoint));
        var widgets = layout.RootElement.GetProperty("widgets").EnumerateArray().ToList();
        Assert.Equal(MemoryFirstThenUnknownThenDefaults, widgets.Select(widget =>
        {
            return widget.GetProperty("key").GetString()!;
        }));
        Assert.True(widgets.Single(widget =>
        {
            return widget.GetProperty("key").GetString() == "cpu";
        }).GetProperty("hidden").GetBoolean());
        var actions = layout.RootElement.GetProperty("actions").EnumerateArray().ToList();
        Assert.Equal("addSite", actions[0].GetProperty("key").GetString());
        Assert.True(actions[0].GetProperty("hidden").GetBoolean());
        var entry = Assert.Single(audit.Entries, written =>
        {
            return written.Action == IdentityAuditActions.DashboardLayoutSaved;
        });
        Assert.True(entry.Succeeded);
        Assert.Equal("widgets=3 actions=1", entry.Subject);
    }

    /// <summary>One person's layout is invisible to another, and a user id in the body is ignored.</summary>
    [Fact]
    public async Task Another_users_layout_is_invisible_and_a_user_id_in_the_body_is_ignored()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "alice", accountId);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var save = await alice.PutAsJsonAsync(Endpoint, new
        {
            Widgets = new[] { new { Key = "cpu", Hidden = true } },
            Actions = Array.Empty<object>(),
            UserId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        Assert.Equal(["cpu"], await WidgetKeysAsync(alice));
        Assert.Equal(AdministratorWidgets, await WidgetKeysAsync(admin));
    }

    /// <summary>A layout outside the bounds is refused with its code, and the stored layout is untouched.</summary>
    [Fact]
    public async Task A_malformed_layout_is_refused_with_its_code_and_stores_nothing()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var badKey = await admin.PutAsJsonAsync(Endpoint, new
        {
            Widgets = new[] { new { Key = "Not A Key", Hidden = false } },
            Actions = Array.Empty<object>(),
        });
        var repeated = await admin.PutAsJsonAsync(Endpoint, new
        {
            Widgets = new[] { new { Key = "cpu", Hidden = false }, new { Key = "cpu", Hidden = true } },
            Actions = Array.Empty<object>(),
        });
        var tooMany = await admin.PutAsJsonAsync(Endpoint, new
        {
            Widgets = Enumerable.Range(0, 33).Select(index =>
            {
                return new { Key = $"w{index}", Hidden = false };
            }).ToArray(),
            Actions = Array.Empty<object>(),
        });
        var missing = await admin.PutAsJsonAsync(Endpoint, new { Widgets = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        Assert.Contains("DashboardLayoutKeyInvalid", await badKey.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Contains("DashboardLayoutKeyRepeated", await repeated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("DashboardLayoutTooLong", await tooMany.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // A list absent from the body never reaches the validator over HTTP: MVC refuses the unbound
        // parameter first, with the panel's generic code. The validator's own NotNull rule
        // (DashboardLayoutListMissing) answers the same command dispatched on the bus, and its unit tests hold it.
        Assert.Contains("RequestParameterInvalid", await missing.Content.ReadAsStringAsync());
        Assert.Equal(AdministratorWidgets, await WidgetKeysAsync(admin));
    }

    /// <summary>An anonymous caller is refused on both verbs.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Endpoint, new { Widgets = Array.Empty<object>(), Actions = Array.Empty<object>() })).StatusCode);
    }

    /// <summary>Reads the caller's widget keys in the order the server lists them.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <returns>The widget keys.</returns>
    private static async Task<List<string>> WidgetKeysAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(Endpoint));

        return body.RootElement.GetProperty("widgets").EnumerateArray()
            .Select(widget =>
            {
                return widget.GetProperty("key").GetString()!;
            })
            .ToList();
    }
}
