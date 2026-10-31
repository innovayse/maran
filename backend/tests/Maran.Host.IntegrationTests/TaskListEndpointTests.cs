using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged tasks list (<c>GET /api/v1/tasks</c>) and its companions — the running list, the failed
/// count and the kind choices — over real HTTP against a real PostgreSQL: the subject search is an
/// <c>ILIKE</c> only PostgreSQL runs, the filters bind from the query string, every bound is refused
/// by a validator, and what a customer may see is decided by the module's query filter on every one
/// of the four routes.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class TaskListEndpointTests : IAsyncLifetime
{
    /// <summary>The instant the seeded tasks start from.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TaskListEndpointTests(PostgresFixture postgres)
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
    /// A search reaches the subject literally: an underscore is not a wildcard, and LIKE
    /// metacharacters with a quote and SQL match nothing.
    /// </summary>
    [Fact]
    public async Task A_search_matches_the_subject_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        await SeedTasksAsync(
            factory,
            NewTask("shop_one.example.com", Epoch),
            NewTask("shopXone.example.com", Epoch));
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var literal = await GetJsonAsync(admin, "/api/v1/tasks?search=SHOP_ONE");
        using var hostile = await GetJsonAsync(admin, "/api/v1/tasks?search=" + Uri.EscapeDataString("%_\\'; drop"));

        Assert.Equal("shop_one.example.com", Assert.Single(Items(literal)).GetProperty("subject").GetString());
        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The status, kind and window filters bind from the query string in the shape the answers
    /// carry, and the window is inclusive at both ends.
    /// </summary>
    [Fact]
    public async Task The_status_kind_and_window_filters_bind_and_keep_only_their_rows()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        var early = NewTask("early.example.com", Epoch);
        var failed = NewTask("failed.example.com", Epoch.AddDays(2), TaskKinds.BackupCreate);
        failed.Fail("AgentSystemFailure", Epoch.AddDays(2));
        var late = NewTask("late.example.com", Epoch.AddDays(5));
        await SeedTasksAsync(factory, early, failed, late);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byStatus = await GetJsonAsync(admin, "/api/v1/tasks?status=failed");
        using var byKind = await GetJsonAsync(admin, "/api/v1/tasks?kind=BackupCreate");
        using var byWindow = await GetJsonAsync(
            admin,
            "/api/v1/tasks?from=" + Uri.EscapeDataString(Epoch.AddDays(2).ToString("O"))
            + "&to=" + Uri.EscapeDataString(Epoch.AddDays(5).ToString("O")));

        Assert.Equal([failed.Id], Ids(byStatus));
        Assert.Equal([failed.Id], Ids(byKind));
        Assert.Equal([late.Id, failed.Id], Ids(byWindow));
    }

    /// <summary>
    /// Newest first by default, the subject sort orders on PostgreSQL in both directions, and a page
    /// past the end is empty with the true total.
    /// </summary>
    [Fact]
    public async Task The_default_order_the_subject_sort_and_a_page_past_the_end_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        var older = NewTask("b.example.com", Epoch);
        var newer = NewTask("a.example.com", Epoch.AddDays(1));
        await SeedTasksAsync(factory, older, newer);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byDefault = await GetJsonAsync(admin, "/api/v1/tasks");
        using var bySubject = await GetJsonAsync(admin, "/api/v1/tasks?sort=subject");
        using var bySubjectDescending = await GetJsonAsync(admin, "/api/v1/tasks?sort=-subject");
        using var beyond = await GetJsonAsync(admin, "/api/v1/tasks?page=3&pageSize=10");

        Assert.Equal([newer.Id, older.Id], Ids(byDefault));
        Assert.Equal([newer.Id, older.Id], Ids(bySubject));
        Assert.Equal([older.Id, newer.Id], Ids(bySubjectDescending));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, beyond.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A bound the list refuses answers 400 with its code.</summary>
    /// <param name="queryString">The refused query string.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=200", "ListPageSizeNotOffered")]
    [InlineData("sort=createdAt", "ListSortUnknown")]
    [InlineData("kind=" + "Backup%25", "TaskKindFilterInvalid")]
    [InlineData("status=paused", "RequestParameterInvalid")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "TaskDateRangeInverted")]
    public async Task A_bound_the_list_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/tasks?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>
    /// The IDOR case: a customer searching, filtering or naming another tenant's account is answered
    /// an empty page with a total of zero, and the running list, the failed count and the kind
    /// choices are of their own visible tasks only — while the administrator's answer to the same
    /// requests is not empty, which is what makes the customer's zeros evidence.
    /// </summary>
    [Fact]
    public async Task A_customer_reaching_for_another_tenants_tasks_gets_nothing_on_any_route()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);
        var ownTask = NewTask("own.example.com", Epoch, TaskKinds.BackupCreate, own, TaskAudience.AccountOwner);
        var strangerTask = NewTask(
            "stranger.example.com", Epoch, TaskKinds.BackupRestore, stranger, TaskAudience.AccountOwner);
        strangerTask.Fail("AgentSystemFailure", Epoch);
        var suspension = NewTask(
            "own", Epoch, TaskKinds.AccountSuspension, own, TaskAudience.AdministratorOnly);
        await SeedTasksAsync(factory, ownTask, strangerTask, suspension);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminBySearch = await GetJsonAsync(admin, "/api/v1/tasks?search=stranger");
        using var bySearch = await GetJsonAsync(customer, "/api/v1/tasks?search=stranger");
        using var byAccount = await GetJsonAsync(customer, $"/api/v1/tasks?accountId={stranger}");
        using var byKind = await GetJsonAsync(customer, "/api/v1/tasks?kind=AccountSuspension");
        using var mine = await GetJsonAsync(customer, "/api/v1/tasks");
        using var running = await GetJsonAsync(customer, "/api/v1/tasks/running");
        using var failed = await GetJsonAsync(customer, "/api/v1/tasks/failed-count");
        using var adminFailed = await GetJsonAsync(admin, "/api/v1/tasks/failed-count");
        using var kinds = await GetJsonAsync(customer, "/api/v1/tasks/kinds");

        Assert.Equal(1, adminBySearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, bySearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byAccount.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byKind.RootElement.GetProperty("total").GetInt32());
        Assert.Equal([ownTask.Id], Ids(mine));
        Assert.Equal(
            [ownTask.Id],
            running.RootElement.EnumerateArray().Select(task => { return task.GetProperty("id").GetGuid(); }));
        Assert.Equal(0, failed.RootElement.GetInt32());
        Assert.Equal(1, adminFailed.RootElement.GetInt32());
        Assert.Equal(
            ["BackupCreate"],
            kinds.RootElement.EnumerateArray().Select(choice => { return choice.GetProperty("kind").GetString(); }));
    }

    /// <summary>Migrates every module these routes touch, seeds the administrator and one account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to store.</param>
    /// <returns>The account's id.</returns>
    private static async Task<Guid> SeedWorldAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        return accountId;
    }

    /// <summary>Stores the given task rows.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="rows">The rows.</param>
    private static async Task SeedTasksAsync(WebApplicationFactory<Program> factory, params PanelTask[] rows)
    {
        // Written through a context resolved outside a request, so no visibility filter separates the
        // seed: the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        tasks.PanelTasks.AddRange(rows);
        await tasks.SaveChangesAsync();
    }

    /// <summary>Builds one running task row.</summary>
    /// <param name="subject">What it acts on.</param>
    /// <param name="startedAt">When it started.</param>
    /// <param name="kind">What kind of operation it records.</param>
    /// <param name="accountId">The account it concerns, or none for server-wide work.</param>
    /// <param name="audience">Who besides an administrator may read it.</param>
    private static PanelTask NewTask(
        string subject,
        DateTimeOffset startedAt,
        string kind = TaskKinds.CertificateIssue,
        Guid? accountId = null,
        TaskAudience audience = TaskAudience.AdministratorOnly)
    {
        return new PanelTask(Guid.NewGuid(), kind, subject, accountId, audience, null, startedAt);
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
