using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Updates.Controllers;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel's own update over real HTTP against real PostgreSQL (issue #382): administrators only, a
/// request that carries nothing, a task and an audit entry per request, and one update at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tenant IDOR test does not apply.</b> The update state is one fact about the whole server; no
/// customer owns it, so there is no other account's row to ask for. What replaces it is the role gate:
/// a customer is answered 403 and not 404, because there is no identifier to hide.
/// </para>
/// <para>
/// <b>What this suite cannot observe:</b> that a root-owned systemd unit starts when the marker appears.
/// That lives in the installer polygon. Here the request directory is an ordinary temporary directory,
/// and the marker is the whole of what the panel does.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PanelUpdateEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/panel-update";

    /// <summary>The check route (issue #516).</summary>
    private const string CheckPath = "/api/v1/panel-update/check";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The temporary directory standing in for the server's update files.</summary>
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"maran-panel-update-tests-{Guid.NewGuid():N}");

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PanelUpdateEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database and directories exist.</returns>
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(_root, "request"));
        Directory.CreateDirectory(System.IO.Path.Combine(_root, "releases"));
        return _pg.CreateAsync();
    }

    /// <summary>Removes the temporary directories.</summary>
    /// <returns>Resolves once they are gone.</returns>
    public Task DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by both routes.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_both_routes()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Path, null)).StatusCode);
        Assert.False(MarkerExists());
    }

    /// <summary>A signed in customer is refused with 403 by both routes and no marker is created.</summary>
    [Fact]
    public async Task A_signed_in_customer_is_refused_by_both_routes_and_no_marker_is_created()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Path, null)).StatusCode);
        Assert.False(MarkerExists());
    }

    /// <summary>An administrator reads the state in the shape the screen declares.</summary>
    [Fact]
    public async Task An_administrator_reads_the_state_in_the_shape_the_screen_declares()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        File.WriteAllText(System.IO.Path.Combine(_root, "release.state"), "CHANNEL=stable\nVERSION=1.0.0\n");
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateObject().Select(property =>
        {
            return property.Name;
        }).Order().ToArray();
        Assert.Equal(
            ["available", "canStart", "channel", "checkInProgress", "currentVersion", "lastCheckedAt", "previousVersion", "updateInProgress"],
            names);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("currentVersion").GetString()));
        Assert.Equal("stable", body.RootElement.GetProperty("channel").GetString());
        Assert.True(body.RootElement.GetProperty("canStart").GetBoolean());
        Assert.False(body.RootElement.GetProperty("updateInProgress").GetBoolean());
        Assert.False(body.RootElement.GetProperty("checkInProgress").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("available").ValueKind);
    }

    /// <summary>A request is accepted with a task id, creates the marker, opens a running task, names the task in the ledger and is journalled.</summary>
    [Fact]
    public async Task A_request_is_accepted_and_creates_the_marker_the_task_the_ledger_and_the_audit_entry()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsync(Path, null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var taskId = body.RootElement.GetProperty("taskId").GetGuid();

        Assert.Equal("update\n", File.ReadAllText(MarkerPath()));
        Assert.Equal(taskId, Guid.Parse((await File.ReadAllTextAsync(System.IO.Path.Combine(_root, "panel-update-task"))).Trim()));

        using var scope = factory.Services.CreateScope();
        var task = await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
            .PanelTasks.IgnoreQueryFilters().SingleAsync(row => row.Id == taskId);
        Assert.Equal(TaskKinds.PanelUpdate, task.Kind);
        Assert.Equal(PanelTaskStatus.Running, task.Status);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);
        Assert.Null(task.AccountId);

        var entry = Assert.Single(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(row => row.Action == UpdatesAuditActions.PanelUpdateRequested)
            .ToListAsync());
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("request=update", entry.Subject);
        Assert.True(entry.Succeeded);

        using var state = JsonDocument.Parse(await client.GetStringAsync(Path));
        Assert.True(state.RootElement.GetProperty("updateInProgress").GetBoolean());
    }

    /// <summary>A second request while the first is queued is refused with 409 and its problem code, and opens no second task.</summary>
    [Fact]
    public async Task A_second_request_while_one_is_queued_is_refused_with_a_conflict()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Path, null)).StatusCode);

        var second = await client.PostAsync(Path, null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("PanelUpdateRunning", problem.RootElement.GetProperty("code").GetString());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(
            1,
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
                .PanelTasks.IgnoreQueryFilters().CountAsync(row => row.Kind == TaskKinds.PanelUpdate));
    }

    /// <summary>No field of the request body is read: a hostile body produces the same marker as no body and journals the real address.</summary>
    [Fact]
    public async Task A_hostile_body_is_not_read_and_produces_the_same_marker()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            Path,
            new { IpAddress = "6.6.6.6", UserAgent = "attacker", CorrelationId = "x", Version = "9.9.9", Url = "https://evil.example/x", Path = "/etc" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("update\n", File.ReadAllText(MarkerPath()));
        using var scope = factory.Services.CreateScope();
        var entry = Assert.Single(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(row => row.Action == UpdatesAuditActions.PanelUpdateRequested)
            .ToListAsync());
        Assert.NotEqual("6.6.6.6", entry.IpAddress);
        Assert.NotEqual("attacker", entry.UserAgent);
    }

    /// <summary>A server whose request directory is missing refuses the request with 503 and creates no task.</summary>
    [Fact]
    public async Task A_server_without_the_request_directory_refuses_the_request_as_unavailable()
    {
        Directory.Delete(System.IO.Path.Combine(_root, "request"));
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsync(Path, null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var state = JsonDocument.Parse(await client.GetStringAsync(Path));
        Assert.False(state.RootElement.GetProperty("canStart").GetBoolean());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(
            0,
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>()
                .PanelTasks.IgnoreQueryFilters().CountAsync(row => row.Kind == TaskKinds.PanelUpdate));
    }

    /// <summary>The check route refuses an anonymous caller with 401 and a customer with 403, and creates no marker.</summary>
    [Fact]
    public async Task The_check_route_refuses_an_anonymous_caller_and_a_customer_and_creates_no_marker()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var anonymous = factory.CreateClient();
        using var customer = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(CheckPath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(CheckPath, null)).StatusCode);
        Assert.False(CheckMarkerExists());
    }

    /// <summary>An administrator's check is accepted with no body, creates only the check marker, is journalled, and the state then reports it in progress.</summary>
    [Fact]
    public async Task A_check_is_accepted_creates_only_the_check_marker_and_is_journalled()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsync(CheckPath, null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("check\n", File.ReadAllText(CheckMarkerPath()));
        Assert.False(MarkerExists(), "a check must never create the update marker");
        using var state = JsonDocument.Parse(await client.GetStringAsync(Path));
        Assert.True(state.RootElement.GetProperty("checkInProgress").GetBoolean());
        Assert.False(state.RootElement.GetProperty("updateInProgress").GetBoolean());
        using var scope = factory.Services.CreateScope();
        var entry = Assert.Single(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(row => row.Action == UpdatesAuditActions.PanelUpdateCheckRequested)
            .ToListAsync());
        Assert.Equal("request=check", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second check while one is queued is refused with 409.</summary>
    [Fact]
    public async Task A_second_check_while_one_is_queued_is_refused_as_a_conflict()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(CheckPath, null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(CheckPath, null)).StatusCode);
    }

    /// <summary>A check on a server whose request directory is missing is refused with 503.</summary>
    [Fact]
    public async Task A_check_on_a_server_without_the_request_directory_is_unavailable()
    {
        Directory.Delete(System.IO.Path.Combine(_root, "request"));
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync(CheckPath, null)).StatusCode);
    }

    /// <summary>Every route the controller declares is covered by this class, so a new route cannot ship without a role test.</summary>
    [Fact]
    public void Every_panel_update_route_is_covered_by_this_class()
    {
        var declared = ControllerRoutes.Declared<PanelUpdateController>();

        Assert.Equal(
            ["GET /api/v1/panel-update", "POST /api/v1/panel-update", "POST /api/v1/panel-update/check"],
            declared.Order().ToArray());
    }

    /// <summary>The full path of the request marker the host under test is configured with.</summary>
    /// <returns>The marker's path.</returns>
    private string MarkerPath()
    {
        return System.IO.Path.Combine(_root, "request", "requested");
    }

    /// <summary>The full path of the check request marker the host under test is configured with.</summary>
    /// <returns>The marker's path.</returns>
    private string CheckMarkerPath()
    {
        return System.IO.Path.Combine(_root, "request", "check-requested");
    }

    /// <summary>Whether the check request marker exists.</summary>
    /// <returns>True when something is at the check marker's name.</returns>
    private bool CheckMarkerExists()
    {
        return File.Exists(CheckMarkerPath());
    }

    /// <summary>Whether the request marker exists.</summary>
    /// <returns>True when something is at the marker's name.</returns>
    private bool MarkerExists()
    {
        return File.Exists(MarkerPath());
    }

    /// <summary>Boots the host against this class's PostgreSQL and temporary directories.</summary>
    /// <returns>The factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("PanelUpdate:ReleaseStatePath", System.IO.Path.Combine(_root, "release.state"));
            builder.UseSetting("PanelUpdate:CheckStatePath", System.IO.Path.Combine(_root, "update-check.state"));
            builder.UseSetting("PanelUpdate:RunningStatePath", System.IO.Path.Combine(_root, "update-running.state"));
            builder.UseSetting("PanelUpdate:ReleasesDirectory", System.IO.Path.Combine(_root, "releases"));
            builder.UseSetting("PanelUpdate:RequestDirectory", System.IO.Path.Combine(_root, "request"));
            builder.UseSetting("PanelUpdate:TaskLedgerPath", System.IO.Path.Combine(_root, "panel-update-task"));
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

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>A client whose requests carry the user's bearer token.</returns>
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
