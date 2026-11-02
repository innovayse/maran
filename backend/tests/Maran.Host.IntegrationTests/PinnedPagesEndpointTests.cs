using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>/api/v1/me/pinned-pages</c> over real HTTP: a person pins, lists and unpins their own pages, a
/// second person never sees them, and every change leaves one audit entry.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PinnedPagesEndpointTests : IAsyncLifetime
{
    /// <summary>The route of the endpoint under test.</summary>
    private const string Endpoint = "/api/v1/me/pinned-pages";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PinnedPagesEndpointTests(PostgresFixture postgres)
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

    /// <summary>A customer pins two pages, reads them back in order and unpins one; each change is audited.</summary>
    [Fact]
    public async Task A_customer_pins_lists_and_unpins_their_pages_and_each_change_is_audited()
    {
        var audit = new RecordingAuditWriter();
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Endpoint, new { ItemKey = "sites-list" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Endpoint, new { ItemKey = "dashboard" })).StatusCode);

        Assert.Equal(new[] { "sites-list", "dashboard" }, await KeysAsync(client));

        var removal = await client.DeleteAsync($"{Endpoint}/sites-list");

        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        Assert.Equal(new[] { "dashboard" }, await KeysAsync(client));
        Assert.Equal(
            new[] { AuditActions.PagePinned, AuditActions.PagePinned, AuditActions.PageUnpinned },
            audit.Entries.Where(entry => entry.Action is AuditActions.PagePinned or AuditActions.PageUnpinned)
                .Select(entry => entry.Action));
        Assert.All(
            audit.Entries.Where(entry => entry.Action is AuditActions.PagePinned or AuditActions.PageUnpinned),
            entry => Assert.True(entry.Succeeded));
    }

    /// <summary>Another person's pins are invisible and unpinning their key answers 404 without touching it.</summary>
    [Fact]
    public async Task Another_users_pins_are_invisible_and_cannot_be_unpinned()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, aliceAccountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, bobAccountId) = await EndpointHarness.SeedAccountAsync(factory, "globex");
        await EndpointHarness.SeedCustomerAsync(factory, "alice", aliceAccountId);
        await EndpointHarness.SeedCustomerAsync(factory, "bob", bobAccountId);
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        using var bob = await EndpointHarness.SignInAsync(factory, "bob");
        await bob.PostAsJsonAsync(Endpoint, new { ItemKey = "backups-list" });

        Assert.Empty(await KeysAsync(alice));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"{Endpoint}/backups-list")).StatusCode);
        Assert.Equal(new[] { "backups-list" }, await KeysAsync(bob));
    }

    /// <summary>An administrator pins too, and a duplicate, a malformed key and a ninth pin are refused with their codes.</summary>
    [Fact]
    public async Task An_administrator_is_refused_a_duplicate_a_malformed_key_and_a_ninth_pin()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await admin.PostAsJsonAsync(Endpoint, new { ItemKey = "dashboard" });
        var duplicate = await admin.PostAsJsonAsync(Endpoint, new { ItemKey = "dashboard" });
        var malformed = await admin.PostAsJsonAsync(Endpoint, new { ItemKey = "Not A Key" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        for (var index = 1; index < 8; index++)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await admin.PostAsJsonAsync(Endpoint, new { ItemKey = $"page-{index}" })).StatusCode);
        }

        var ninth = await admin.PostAsJsonAsync(Endpoint, new { ItemKey = "page-9" });

        Assert.Equal(HttpStatusCode.Conflict, ninth.StatusCode);
        Assert.Contains("PinnedPageLimitReached", await ninth.Content.ReadAsStringAsync());
        Assert.Equal(8, (await KeysAsync(admin)).Count);
    }

    /// <summary>A caller cannot aim a pin at another user by naming one in the body.</summary>
    [Fact]
    public async Task A_user_id_in_the_body_is_ignored()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "alice", accountId);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        await alice.PostAsJsonAsync(Endpoint, new { ItemKey = "sites-list", UserId = Guid.NewGuid() });

        Assert.Equal(new[] { "sites-list" }, await KeysAsync(alice));
        Assert.Empty(await KeysAsync(admin));
    }

    /// <summary>Reads the caller's pinned keys in the order the server lists them.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <returns>The item keys.</returns>
    private static async Task<List<string>> KeysAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(Endpoint));

        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("itemKey").GetString()!)
            .ToList();
    }
}
