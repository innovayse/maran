using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The tasks surface for a customer, over real HTTP against real PostgreSQL: a customer reads the
/// tasks recorded against their own account, never another tenant's, and never a log.
/// </summary>
/// <remarks>
/// <para>
/// The tasks are recorded through the composed panel's own <see cref="ITaskRecorder"/>, with the
/// account id the backup handlers pass, so the write half is the production path and only the
/// operation behind it is left out — a backup would need the agent, and what is under test here is
/// who may read the row it leaves.
/// </para>
/// <para>
/// Three rows are seeded: a backup of the signed-in customer's account, a backup of another tenant's,
/// and a suspension of the customer's OWN account — which concerns them and is still
/// administrator-only, so it must stay hidden although its account id matches. Each test asserts both sides — what the
/// customer may see and what they may not — so neither "everything is hidden" nor "everything is
/// shown" can pass it.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TasksCustomerScopeTests : IAsyncLifetime
{
    /// <summary>The operator-facing line every seeded task's log carries.</summary>
    private const string LogLine = "agent answered with tool output";

    /// <summary>The bound on reading a stream to its end.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TasksCustomerScopeTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once the database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A customer lists only the task recorded against their own account, with an empty log.</summary>
    [Fact]
    public async Task A_customer_lists_only_the_task_recorded_against_their_own_account_with_an_empty_log()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        using var response = await client.GetAsync("/api/v1/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var task = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(world.OwnTaskId, task.GetProperty("id").GetGuid());
        Assert.False(task.GetProperty("logIncluded").GetBoolean());
        Assert.Equal(string.Empty, task.GetProperty("log").GetString());
    }

    /// <summary>A customer reads their own task by id with an empty log, and another tenant's answers not found.</summary>
    /// <remarks>
    /// The IDOR case (rules/testing.md item 3): 404, never 403, on the single read AND on the stream,
    /// which are two doors to the same row. The customer's own task answering 200 on the same route
    /// is the control that keeps the 404s from passing because the route is simply broken.
    /// </remarks>
    [Fact]
    public async Task Another_tenants_task_answers_not_found_on_the_single_read_and_the_stream()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        using var own = await client.GetAsync($"/api/v1/tasks/{world.OwnTaskId}");
        using var single = await client.GetAsync($"/api/v1/tasks/{world.OtherTenantTaskId}");
        using var stream = await client.GetAsync($"/api/v1/tasks/{world.OtherTenantTaskId}/stream");
        using var adminOnly = await client.GetAsync($"/api/v1/tasks/{world.AdministratorTaskId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using (var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync()))
        {
            Assert.False(body.RootElement.GetProperty("logIncluded").GetBoolean());
            Assert.Equal(string.Empty, body.RootElement.GetProperty("log").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stream.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, adminOnly.StatusCode);
    }

    /// <summary>A customer never sees an administrator-only task about their own account.</summary>
    /// <remarks>
    /// The suspension carries the customer's own account id, so the account half of the filter
    /// matches it; only the audience keeps it out. The administrator listing it is the control that
    /// the row exists and is found by its account.
    /// </remarks>
    [Fact]
    public async Task A_customer_never_sees_an_administrator_only_task_about_their_own_account()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var customerIds = await ListIdsAsync(customer);
        var adminIds = await ListIdsAsync(admin);

        Assert.DoesNotContain(world.AdministratorTaskId, customerIds);
        Assert.Contains(world.AdministratorTaskId, adminIds);
    }

    /// <summary>Lists the task ids the client may see.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <returns>The ids.</returns>
    private static async Task<List<Guid>> ListIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/tasks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.EnumerateArray()
            .Select(task =>
            {
                return task.GetProperty("id").GetGuid();
            })
            .ToList();
    }

    /// <summary>The frames a customer is streamed for their own task carry no log, while an administrator's do.</summary>
    /// <remarks>
    /// The administrator's read of the same stream is the positive control: it proves the seeded log
    /// really reaches the wire, so its absence from the customer's frames is redaction and not a
    /// task that never had a log.
    /// </remarks>
    [Fact]
    public async Task The_frames_a_customer_is_streamed_carry_no_log_while_an_administrators_do()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var customerFrames = await ReadStreamAsync(customer, world.OwnTaskId);
        var adminFrames = await ReadStreamAsync(admin, world.OwnTaskId);

        Assert.Contains("event: task", customerFrames, StringComparison.Ordinal);
        Assert.Contains("\"log\":\"\"", customerFrames, StringComparison.Ordinal);
        Assert.Contains("\"logIncluded\":false", customerFrames, StringComparison.Ordinal);
        Assert.DoesNotContain("\"logIncluded\":true", customerFrames, StringComparison.Ordinal);
        Assert.DoesNotContain(LogLine, customerFrames, StringComparison.Ordinal);
        Assert.Contains(LogLine, adminFrames, StringComparison.Ordinal);
        Assert.Contains("\"logIncluded\":true", adminFrames, StringComparison.Ordinal);
    }

    /// <summary>Reads a finished task's stream to its end.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="taskId">The task to watch; it has already finished, so the stream ends by itself.</param>
    /// <returns>Everything the stream sent.</returns>
    private static async Task<string> ReadStreamAsync(HttpClient client, Guid taskId)
    {
        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var response = await client.GetAsync($"/api/v1/tasks/{taskId}/stream", deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync(deadline.Token);
    }

    /// <summary>Migrates, seeds two tenants, their logins, an administrator and three finished tasks.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The ids of the three tasks.</returns>
    private static async Task<SeededTasks> SeedAsync(WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        }

        var (_, ownAccountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, otherAccountId) = await EndpointHarness.SeedAccountAsync(factory, "other");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", ownAccountId);
        await EndpointHarness.SeedAdministratorAsync(factory);

        var own = await RecordFinishedTaskAsync(
            factory, TaskKinds.BackupCreate, "acme", ownAccountId, TaskAudience.AccountOwner);
        var other = await RecordFinishedTaskAsync(
            factory, TaskKinds.BackupCreate, "other", otherAccountId, TaskAudience.AccountOwner);
        var administrator = await RecordFinishedTaskAsync(
            factory, TaskKinds.AccountSuspension, "acme", ownAccountId, TaskAudience.AdministratorOnly);

        return new SeededTasks(own, other, administrator);
    }

    /// <summary>Records one task through the panel's recorder, reports a log line and completes it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="kind">The task's kind.</param>
    /// <param name="subject">The task's subject.</param>
    /// <param name="accountId">The account it concerns.</param>
    /// <param name="audience">Who besides an administrator may read it.</param>
    /// <returns>The task's id.</returns>
    private static async Task<Guid> RecordFinishedTaskAsync(
        WebApplicationFactory<Program> factory,
        string kind,
        string subject,
        Guid accountId,
        TaskAudience audience)
    {
        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ITaskRecorder>();

        var id = await recorder.BeginAsync(
            kind, subject, accountId, audience, correlationId: null, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, id);
        await recorder.ReportAsync(id, 50, LogLine, CancellationToken.None);
        await recorder.CompleteAsync(id, CancellationToken.None);

        return id;
    }

    /// <summary>The three tasks a test reads.</summary>
    /// <param name="OwnTaskId">Recorded against the signed-in customer's account.</param>
    /// <param name="OtherTenantTaskId">Recorded against another tenant's account.</param>
    /// <param name="AdministratorTaskId">A suspension of the customer's own account, administrator-only.</param>
    private sealed record SeededTasks(Guid OwnTaskId, Guid OtherTenantTaskId, Guid AdministratorTaskId);
}
