using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged plan list (<c>GET /api/v1/plans</c>) and the plan picker
/// (<c>GET /api/v1/plans/choices</c>) over real HTTP against a real PostgreSQL: the query-string
/// binding, the validator's bounds and the answer's shape, end to end.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PlanListEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PlanListEndpointTests(PostgresFixture postgres)
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
    /// The list answers the paged shape, searched by name ignoring case, with each plan's account count.
    /// </summary>
    [Fact]
    public async Task The_list_answers_the_paged_shape_searched_by_name_with_each_plans_account_count()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var agency = await SeedAsync(factory, ["Agency", "Studio", "Starter"]);
        await EndpointHarness.SeedAccountAsync(factory, "olive", agency);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/plans?search=AGEN");

        Assert.Equal(1, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, page.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, page.RootElement.GetProperty("pageSize").GetInt32());
        var row = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Agency", row.GetProperty("displayName").GetString());
        Assert.Equal(1, row.GetProperty("accountCount").GetInt32());
    }

    /// <summary>
    /// Every sort key orders the list, and the descending order is the ascending one reversed. Each
    /// key is unique across the seeded rows, so the order is the key's alone and not the tie-breaker's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The names in ascending order of that key.</param>
    [Theory]
    [InlineData("name", new[] { "Agency", "Starter", "Studio" })]
    [InlineData("diskQuota", new[] { "Studio", "Agency", "Starter" })]
    [InlineData("accounts", new[] { "Starter", "Agency", "Studio" })]
    public async Task Every_sort_key_orders_the_list_in_both_directions(string field, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        // Disk grows in the order given: Studio, Agency, Starter.
        await SeedAsync(factory, ["Studio", "Agency", "Starter"]);
        // Accounts: Starter none, Agency one, Studio two.
        await EndpointHarness.SeedAccountAsync(factory, "olive", await PlanAsync(factory, "Agency"));
        var studio = await PlanAsync(factory, "Studio");
        await EndpointHarness.SeedAccountAsync(factory, "pepper", studio);
        await EndpointHarness.SeedAccountAsync(factory, "basil", studio);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, "/api/v1/plans?sort=" + field);
        using var descending = await GetJsonAsync(admin, "/api/v1/plans?sort=-" + field);

        Assert.Equal(expected, Names(ascending));
        Assert.Equal(expected.Reverse(), Names(descending));
    }

    /// <summary>A page holds the remainder, and a page past the end is empty with the true total.</summary>
    [Fact]
    public async Task A_page_holds_the_remainder_and_a_page_past_the_end_is_empty_with_the_true_total()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, Enumerable.Range(1, 12).Select(index => { return $"Tier{index:D2}"; }).ToArray());
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, "/api/v1/plans?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, "/api/v1/plans?page=3&pageSize=10");

        Assert.Equal(["Tier11", "Tier12"], Names(last));
        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A sort, a page or a page size outside the contract answers 400 with its code.</summary>
    /// <param name="queryString">The refused parameter.</param>
    /// <param name="code">The code it answers.</param>
    [Theory]
    [InlineData("sort=maxSites", "ListSortUnknown")]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=7", "ListPageSizeNotOffered")]
    [InlineData("search=al%0Apha", "ListSearchInvalid")]
    public async Task A_parameter_outside_the_contract_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["Agency"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/plans?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>
    /// The picker answers identity, name and the two printed limits only, alphabetically, searched by name.
    /// </summary>
    [Fact]
    public async Task The_picker_answers_identity_name_and_the_two_printed_limits_searched_by_name()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["Studio", "Agency", "Starter"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var all = await GetJsonAsync(admin, "/api/v1/plans/choices");
        using var narrowed = await GetJsonAsync(admin, "/api/v1/plans/choices?search=st");

        Assert.Equal(["Agency", "Starter", "Studio"], Names(all));
        Assert.Equal(["Starter", "Studio"], Names(narrowed));
        var first = all.RootElement.GetProperty("items")[0];
        Assert.Equal(
            ["id", "displayName", "diskQuotaMb", "maxSites"],
            first.EnumerateObject().Select(property => { return property.Name; }));
    }

    /// <summary>The picker is capped by the same contract: it cannot be asked for every plan at once.</summary>
    [Fact]
    public async Task The_picker_is_capped_by_the_same_contract()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["Agency"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/plans/choices?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListPageSizeNotOffered", await CodeOfAsync(response));
    }

    /// <summary>One plan reads by id with its account count, and an unknown id answers 404.</summary>
    [Fact]
    public async Task One_plan_reads_by_id_with_its_account_count_and_an_unknown_id_answers_404()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var agency = await SeedAsync(factory, ["Agency"]);
        await EndpointHarness.SeedAccountAsync(factory, "olive", agency);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var plan = await GetJsonAsync(admin, $"/api/v1/plans/{agency.Id}");
        var missing = await admin.GetAsync($"/api/v1/plans/{Guid.NewGuid()}");

        Assert.Equal("Agency", plan.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(1, plan.RootElement.GetProperty("accountCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("PlanNotFound", await CodeOfAsync(missing));
    }

    /// <summary>
    /// Migrates, seeds the administrator, and stores operator plans with disk quotas growing in the
    /// order given.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="names">The plans to store.</param>
    /// <returns>The first plan stored.</returns>
    private static async Task<Plan> SeedAsync(WebApplicationFactory<Program> factory, string[] names)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var plans = names
            .Select((name, index) =>
            {
                return Plan.Define(Guid.NewGuid(), name, 1_024 * (index + 1), 5, 2, 3, 0, 5, 5);
            })
            .ToList();
        context.Plans.AddRange(plans);
        await context.SaveChangesAsync();

        return plans[0];
    }

    /// <summary>Reads one stored plan by its operator name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="name">The plan's name.</param>
    /// <returns>The plan.</returns>
    private static async Task<Plan> PlanAsync(WebApplicationFactory<Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return await context.Plans.SingleAsync(plan => plan.Name == name);
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    /// <returns>The parsed body.</returns>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The plan names on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    /// <returns>The names.</returns>
    private static List<string> Names(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("displayName").GetString()!; })
            .ToList();
    }

    /// <summary>Reads the machine code of a problem answer.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The code.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }
}
