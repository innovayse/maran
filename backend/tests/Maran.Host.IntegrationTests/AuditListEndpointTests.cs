using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged audit journal (<c>GET /api/v1/audit</c>) and its action choices
/// (<c>GET /api/v1/audit/actions</c>) over real HTTP against a real PostgreSQL: the search is an
/// <c>ILIKE</c> only PostgreSQL runs, the enum and window filters bind from the query string, and
/// every bound is refused by a validator before a handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AuditListEndpointTests : IAsyncLifetime
{
    /// <summary>The instant the seeded entries count from, well before any sign-in the test makes.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuditListEndpointTests(PostgresFixture postgres)
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
    /// A search reaches the actor, the subject and the address, literally: an underscore is not a
    /// wildcard, and LIKE metacharacters with a quote and SQL match nothing.
    /// </summary>
    [Fact]
    public async Task A_search_matches_actor_subject_and_address_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(
            factory,
            Entry("olive_ops", "SiteCreated", "shop.example.com", "198.51.100.23", true, Epoch),
            Entry("pepper", "SiteDeleted", "blog.example.com", "192.0.2.77", true, Epoch));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byActor = await GetJsonAsync(admin, "/api/v1/audit?search=OLIVE_");
        using var bySubject = await GetJsonAsync(admin, "/api/v1/audit?search=blog.example");
        using var byAddress = await GetJsonAsync(admin, "/api/v1/audit?search=198.51.100");
        using var wildcard = await GetJsonAsync(admin, "/api/v1/audit?search=" + Uri.EscapeDataString("olive%ops"));
        using var hostile = await GetJsonAsync(admin, "/api/v1/audit?search=" + Uri.EscapeDataString("%_\\'; drop"));

        Assert.Equal("olive_ops", Assert.Single(Items(byActor)).GetProperty("actorUsername").GetString());
        Assert.Equal("blog.example.com", Assert.Single(Items(bySubject)).GetProperty("subject").GetString());
        Assert.Equal("198.51.100.23", Assert.Single(Items(byAddress)).GetProperty("ipAddress").GetString());
        Assert.Equal(0, wildcard.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The action, outcome and window filters bind from the query string in the shape the answers
    /// carry, and the window is inclusive at both ends.
    /// </summary>
    [Fact]
    public async Task The_action_outcome_and_window_filters_bind_and_keep_only_their_rows()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var created = Entry("olive", "SiteCreated", "early.example.com", "192.0.2.1", true, Epoch);
        var refused = Entry("olive", "SiteDeleted", "refused.example.com", "192.0.2.1", false, Epoch.AddDays(2));
        var late = Entry("olive", "SiteDeleted", "late.example.com", "192.0.2.1", true, Epoch.AddDays(5));
        await SeedWorldAsync(factory, created, refused, late);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byAction = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated");
        using var byOutcome = await GetJsonAsync(admin, "/api/v1/audit?outcome=failed&action=SiteDeleted");
        using var byWindow = await GetJsonAsync(
            admin,
            "/api/v1/audit?from=" + Uri.EscapeDataString(Epoch.AddDays(2).ToString("O"))
            + "&to=" + Uri.EscapeDataString(Epoch.AddDays(5).ToString("O")));

        Assert.Equal([created.Id], Ids(byAction));
        Assert.Equal([refused.Id], Ids(byOutcome));
        Assert.Equal([late.Id, refused.Id], Ids(byWindow));
    }

    /// <summary>
    /// Newest first by default, the actor sort orders on PostgreSQL in both directions, and a page
    /// past the end is empty with the true total.
    /// </summary>
    [Fact]
    public async Task The_default_order_the_actor_sort_and_a_page_past_the_end_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var older = Entry("zed", "SiteCreated", "a.example.com", "192.0.2.1", true, Epoch);
        var newer = Entry("amy", "SiteCreated", "b.example.com", "192.0.2.1", true, Epoch.AddDays(1));
        await SeedWorldAsync(factory, older, newer);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byDefault = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated");
        using var byActor = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated&sort=actor");
        using var byActorDescending = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated&sort=-actor");
        using var beyond = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated&page=3&pageSize=10");

        Assert.Equal([newer.Id, older.Id], Ids(byDefault));
        Assert.Equal([newer.Id, older.Id], Ids(byActor));
        Assert.Equal([older.Id, newer.Id], Ids(byActorDescending));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, beyond.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A bound the journal refuses answers 400 with its code.</summary>
    /// <param name="queryString">The refused query string.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=500", "ListPageSizeNotOffered")]
    [InlineData("sort=userAgent", "ListSortUnknown")]
    [InlineData("action=" + "Site%25", "AuditActionFilterInvalid")]
    [InlineData("outcome=sideways", "RequestParameterInvalid")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "AuditDateRangeInverted")]
    public async Task A_bound_the_journal_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/audit?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>The action choices name each action that occurred once, beside its machine name.</summary>
    [Fact]
    public async Task The_action_choices_name_each_action_that_occurred_once()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(
            factory,
            Entry("olive", "SiteCreated", "a.example.com", "192.0.2.1", true, Epoch),
            Entry("olive", "SiteCreated", "b.example.com", "192.0.2.1", true, Epoch));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var choices = await GetJsonAsync(admin, "/api/v1/audit/actions");

        var siteCreated = choices.RootElement.EnumerateArray()
            .Where(choice =>
            {
                return choice.GetProperty("action").GetString() == "SiteCreated";
            })
            .ToList();
        Assert.False(string.IsNullOrEmpty(Assert.Single(siteCreated).GetProperty("actionName").GetString()));
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
    /// <param name="address">Where it came from.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="at">When it happened.</param>
    private static AuditEvent Entry(
        string actor,
        string action,
        string subject,
        string address,
        bool succeeded,
        DateTimeOffset at)
    {
        return new AuditEvent(Guid.NewGuid(), at, null, actor, action, subject, address, "test", succeeded, null);
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

    /// <summary>The rows on a page.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<JsonElement> Items(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>The ids on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<Guid> Ids(JsonDocument page)
    {
        return Items(page)
            .Select(item => { return item.GetProperty("id").GetGuid(); })
            .ToList();
    }

    /// <summary>Reads the machine code of a problem answer.</summary>
    /// <param name="response">The refused response.</param>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }
}
