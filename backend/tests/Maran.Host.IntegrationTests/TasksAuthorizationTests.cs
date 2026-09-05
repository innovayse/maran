using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Tasks.Controllers;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The tasks surface over real HTTP against real PostgreSQL: who may reach the panel's background
/// tasks feed.
/// </summary>
/// <remarks>
/// <para>
/// <c>TasksController</c>'s own remarks state the shape this fixture proves: every kind of task in
/// v1 is an administrator's operation, but the controller is attributed
/// <c>AuthorizationPolicies.AnyAuthenticated</c> rather than <c>AdminOnly</c>, so a customer never
/// gets a 403 that would confirm an administrator-only feed exists here. Instead
/// <c>TasksDbContext</c>'s own query filter (<c>HasQueryFilter(task =&gt; _currentUser.IsAdmin)</c>)
/// makes every row invisible to anyone who is not an administrator, and the handlers answer
/// <c>TaskNotFound</c> — a 404 that confirms nothing (spec §8, rules/testing.md item 3).
/// </para>
/// <para>
/// This is the same 404-never-403 rule the tenant fixtures prove, applied along a different axis: not
/// "customer A cannot see customer B's row" but "a customer cannot see the administrators' feed at
/// all", including a row that genuinely exists. <c>TaskStreamTests</c> already proves the SSE route
/// works end to end for an administrator; what is missing, and what this fixture adds, is the
/// customer side of every route and the completeness proof that a new one cannot go untested.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TasksAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route the tasks controller declares.</summary>
    /// <remarks>
    /// Completeness is asserted by <see cref="Every_tasks_route_is_covered_by_the_gating_fixture"/>
    /// rather than trusted: a new route added without a row here would otherwise enjoy no proof that
    /// it is closed to a customer at all.
    /// </remarks>
    public static IReadOnlyList<string> Endpoints()
    {
        return
        [
            "GET /api/v1/tasks",
            "GET /api/v1/tasks/{id}",
            "GET /api/v1/tasks/{id}/stream",
        ];
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TasksAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every tasks endpoint.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_every_tasks_endpoint()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var taskId = await SeedRunningTaskAsync(factory);
        using var client = factory.CreateClient();

        await AssertEveryEndpointAsync(client, taskId, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A signed in customer is answered not found by every tasks endpoint, including a task that
    /// genuinely exists.
    /// </summary>
    [Fact]
    public async Task A_signed_in_customer_is_answered_not_found_by_every_tasks_endpoint()
    {
        // 404, never 403: the module's own query filter hides every row from a non-administrator,
        // so the answer must be the same whether the id names a real task or one nobody ever
        // created — a 403 here would be the leak, confirming an administrator-only feed exists.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var taskId = await SeedRunningTaskAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        await AssertEveryEndpointAsync(client, taskId, HttpStatusCode.NotFound);
    }

    /// <summary>A task identifier nobody created is answered not found rather than failing.</summary>
    [Fact]
    public async Task A_task_identifier_nobody_created_is_answered_not_found_rather_than_failing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        await AssertEveryEndpointAsync(client, Guid.NewGuid(), HttpStatusCode.NotFound);
    }

    /// <summary>An administrator reaches the tasks feed on every route.</summary>
    [Fact]
    public async Task An_administrator_reaches_the_tasks_feed_on_every_route()
    {
        // Guards the tests above from passing for the wrong reason: if the routes were simply
        // broken, or the seed never ran, "not found" would be true of every request ever made.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var taskId = await SeedRunningTaskAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var list = await client.GetAsync("/api/v1/tasks");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var single = await client.GetAsync($"/api/v1/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
    }

    /// <summary>Every tasks route is covered by the gating fixture.</summary>
    /// <remarks>
    /// The fixture above is a hand-written list, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off the controller itself, so a new
    /// tasks endpoint fails HERE — naming itself — rather than quietly enjoying no not-found
    /// coverage.
    /// </remarks>
    [Fact]
    public void Every_tasks_route_is_covered_by_the_gating_fixture()
    {
        var declared = ControllerRoutes.Declared<TasksController>();
        Assert.NotEmpty(declared);

        var inFixture = Endpoints().ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These tasks routes are absent from Endpoints(), so nothing proves they answer 404 "
            + "rather than 403 for a customer: " + string.Join(", ", missing));
    }

    /// <summary>Asserts that every tasks endpoint answers one status for one task identifier.</summary>
    /// <remarks>
    /// Every route is tried before anything is asserted, and each wrong answer is reported with its
    /// own method and path. Failing on the first would hide the rest, and "one route answers
    /// wrongly" and "every route answers wrongly" are different defects that a first-failure
    /// assertion reports identically.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="taskId">The task identifier to substitute into <c>{id}</c>.</param>
    /// <param name="expected">The status every route must answer.</param>
    private static async Task AssertEveryEndpointAsync(
        HttpClient client,
        Guid taskId,
        HttpStatusCode expected)
    {
        var wrong = new List<string>();
        foreach (var route in Endpoints())
        {
            var parts = route.Split(' ', 2);
            var path = parts[1].Replace("{id}", taskId.ToString(), StringComparison.Ordinal);

            using var request = new HttpRequestMessage(new HttpMethod(parts[0]), path);
            using var response = await client.SendAsync(request);

            if (response.StatusCode != expected)
            {
                wrong.Add($"{route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            $"These routes did not answer {(int)expected}: " + string.Join("; ", wrong));
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one running task directly, bypassing the module's own query filter — the filter admits
    /// only an administrator, and this write is nobody at all.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The seeded task's id.</returns>
    private static async Task<Guid> SeedRunningTaskAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var task = new PanelTask(Guid.NewGuid(), "CertificateIssue", "example.com", null, clock.UtcNow);
        dbContext.PanelTasks.Add(task);
        await dbContext.SaveChangesAsync();

        return task.Id;
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
