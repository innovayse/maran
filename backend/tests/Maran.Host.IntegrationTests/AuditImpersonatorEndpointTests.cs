using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The operator who viewed the panel as an account (issue #327) over real HTTP against a real
/// PostgreSQL: the journal carries the operator's name on the entries written while viewing and
/// <c>null</c> on the rest, and the search finds the operator by name, literally, without matching an
/// entry that has no operator. The search is an <c>ILIKE</c> only PostgreSQL runs.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AuditImpersonatorEndpointTests : IAsyncLifetime
{
    /// <summary>The instant the seeded entries count from, well before any sign-in the test makes.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuditImpersonatorEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// An entry written while viewing as an account carries the operator's name on the wire, and an
    /// entry the actor wrote as themselves carries <c>null</c>, not an empty string.
    /// </summary>
    [Fact]
    public async Task An_entry_written_while_viewing_as_carries_the_operator_and_the_others_carry_null()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var viewed = Entry("olive", "SiteCreated", "viewed.example.com", Epoch.AddMinutes(1), "operator_one");
        var own = Entry("olive", "SiteCreated", "own.example.com", Epoch, null);
        await SeedWorldAsync(factory, viewed, own);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated");

        var items = page.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("operator_one", items[0].GetProperty("impersonatorUsername").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("impersonatorUsername").ValueKind);
    }

    /// <summary>
    /// Searching for the operator's name finds the entry written while they viewed as an account, and
    /// only that one: an entry with no operator is not matched, and an underscore is not a wildcard.
    /// </summary>
    [Fact]
    public async Task A_search_finds_the_operator_literally_and_never_an_entry_without_one()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var viewed = Entry("olive", "SiteCreated", "viewed.example.com", Epoch.AddMinutes(1), "operator_one");
        var lookalike = Entry("olive", "SiteCreated", "other.example.com", Epoch.AddMinutes(2), "operatorXone");
        var own = Entry("olive", "SiteCreated", "own.example.com", Epoch, null);
        await SeedWorldAsync(factory, viewed, lookalike, own);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byOperator = await GetJsonAsync(admin, "/api/v1/audit?search=OPERATOR_ONE");
        using var nobody = await GetJsonAsync(admin, "/api/v1/audit?search=" + Uri.EscapeDataString("operator%one"));

        Assert.Equal([viewed.Id], Ids(byOperator));
        Assert.Equal(0, nobody.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>Migrates, seeds the administrator, and stores the given journal entries.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="entries">The entries to store.</param>
    private static async Task SeedWorldAsync(WebApplicationFactory<Program> factory, params AuditEvent[] entries)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.AuditEvents.AddRange(entries);
        await identity.SaveChangesAsync();
    }

    /// <summary>Builds one journal entry.</summary>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="action">What was attempted.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="operatorName">The operator viewing as the actor, or null when there was none.</param>
    private static AuditEvent Entry(string actor, string action, string subject, DateTimeOffset at, string? operatorName)
    {
        AuditImpersonator? impersonator = operatorName is null
            ? null
            : new AuditImpersonator(Guid.NewGuid(), operatorName);

        return new AuditEvent(
            Guid.NewGuid(), at, null, actor, action, subject, "192.0.2.1", "test", true, null, impersonator);
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The ids on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<Guid> Ids(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("id").GetGuid(); })
            .ToList();
    }
}
