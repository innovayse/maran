using System.Net;
using System.Net.Http.Headers;
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
/// The shell's one running-tasks stream over real HTTP against real PostgreSQL:
/// <c>GET /api/v1/tasks/running/stream</c> delivers frames for every running task the caller may see
/// on a single connection, and none for a task they may not (issue #274).
/// </summary>
/// <remarks>
/// Four running tasks are recorded through the panel's own <see cref="ITaskRecorder"/>: two backups of
/// the signed-in customer's account, a backup of another tenant's, and a suspension of the customer's
/// own account, which is administrator-only. The customer's stream must carry exactly their two; the
/// administrator's stream, carrying all four, is the positive control that the other two exist, are
/// running and would be streamed to somebody allowed to see them — without it, "the customer did not
/// receive them" would also pass on a stream that sent nothing at all.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class RunningTasksStreamTests : IAsyncLifetime
{
    /// <summary>The URL of the stream under test.</summary>
    private const string StreamPath = "/api/v1/tasks/running/stream";

    /// <summary>A failure code Ssl's own table describes, raised outside the Tasks module.</summary>
    private const string FailureCode = "AcmeAccountRejected";

    /// <summary>The Armenian sentence Ssl's table writes for <see cref="FailureCode"/>.</summary>
    private const string ArmenianSentence = "Վկայագրման կենտրոնը մերժեց այս սերվերի գրանցումը։";

    /// <summary>The bound on every stream read, so a missing frame fails rather than hangs.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public RunningTasksStreamTests(PostgresFixture postgres)
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

    /// <summary>
    /// One stream delivers a frame for each running task the customer may see, names exactly those in
    /// its running set, and carries nothing about another tenant's task or an administrator-only one.
    /// </summary>
    [Fact]
    public async Task One_stream_delivers_every_running_task_the_customer_may_see_and_none_they_may_not()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var opening = await ReadOpeningAsync(client);

        Assert.Equal(HttpStatusCode.OK, opening.Status);
        Assert.Equal(new[] { world.OwnFirst, world.OwnSecond }.Order(), opening.TaskIds.Order());
        Assert.Equal(new[] { world.OwnFirst, world.OwnSecond }.Order(), opening.RunningIds.Order());
        Assert.DoesNotContain(world.OtherTenant, opening.TaskIds);
        Assert.DoesNotContain(world.AdministratorOnly, opening.TaskIds);
        Assert.All(opening.Logs, log =>
        {
            Assert.Equal(string.Empty, log);
        });
    }

    /// <summary>The administrator's stream carries all four running tasks: the control for the test above.</summary>
    [Fact]
    public async Task The_administrators_stream_carries_every_running_task()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var opening = await ReadOpeningAsync(client);

        var all = new[] { world.OwnFirst, world.OwnSecond, world.OtherTenant, world.AdministratorOnly }.Order();
        Assert.Equal(all, opening.TaskIds.Order());
        Assert.Equal(all, opening.RunningIds.Order());
    }

    /// <summary>
    /// A task that finishes while the stream is open is sent once more as it ended, and then leaves
    /// the running set — on the same connection, with no second request.
    /// </summary>
    [Fact]
    public async Task A_task_finishing_is_sent_as_it_ended_and_then_leaves_the_running_set()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var response = await client.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        await ReadUntilRunningSetAsync(reader, deadline.Token);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskRecorder>()
                .CompleteAsync(world.OwnFirst, CancellationToken.None);
        }

        var finished = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("task", finished.Name);
        Assert.Equal(world.OwnFirst, finished.Payload.GetProperty("id").GetGuid());
        Assert.Equal("completed", finished.Payload.GetProperty("status").GetString());

        var set = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("running", set.Name);
        Assert.Equal([world.OwnSecond], RunningIdsOf(set.Payload));
    }

    /// <summary>
    /// A task that fails while the stream is open is sent as it ended with its failure described in the
    /// subscriber's own language, the code beside the sentence — not the bare code, not English.
    /// </summary>
    [Fact]
    public async Task A_task_failing_is_sent_with_its_failure_described_in_the_subscribers_language()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var world = await SeedAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "customer");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hy"));

        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var response = await client.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        var (opening, _) = await ReadUntilRunningSetAsync(reader, deadline.Token);
        // The stream's writer omits a null member, so a running task's frame carries no failure at all.
        Assert.All(opening, task =>
        {
            Assert.False(task.TryGetProperty("failure", out var failure) && failure.ValueKind != JsonValueKind.Null);
        });

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskRecorder>()
                .FailAsync(world.OwnFirst, FailureCode, CancellationToken.None);
        }

        var failed = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("task", failed.Name);
        Assert.Equal(world.OwnFirst, failed.Payload.GetProperty("id").GetGuid());
        Assert.Equal("failed", failed.Payload.GetProperty("status").GetString());
        var failure = failed.Payload.GetProperty("failure");
        Assert.Equal(FailureCode, failure.GetProperty("code").GetString());
        Assert.Equal(ArmenianSentence, failure.GetProperty("message").GetString());
    }

    /// <summary>Opens the stream and reads everything up to and including its first running set.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <returns>The status, the ids and logs of the task frames, and the ids the set named.</returns>
    private static async Task<Opening> ReadOpeningAsync(HttpClient client)
    {
        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var response = await client.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));

        var (tasks, runningIds) = await ReadUntilRunningSetAsync(reader, deadline.Token);
        var ids = new List<Guid>();
        var logs = new List<string>();
        foreach (var task in tasks)
        {
            ids.Add(task.GetProperty("id").GetGuid());
            logs.Add(task.GetProperty("log").GetString() ?? string.Empty);
        }

        return new Opening(response.StatusCode, ids, logs, runningIds);
    }

    /// <summary>Reads task frames until the first running set arrives.</summary>
    /// <param name="reader">The response body reader.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The task payloads read, and the ids the set named.</returns>
    private static async Task<(List<JsonElement> Tasks, List<Guid> RunningIds)> ReadUntilRunningSetAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var tasks = new List<JsonElement>();
        while (true)
        {
            var frame = await ReadEventAsync(reader, cancellationToken);
            if (frame.Name == "running")
            {
                return (tasks, RunningIdsOf(frame.Payload));
            }

            Assert.Equal("task", frame.Name);
            tasks.Add(frame.Payload);
        }
    }

    /// <summary>Reads the ids a running-set payload names.</summary>
    /// <param name="payload">The payload of a <c>running</c> event.</param>
    /// <returns>The ids, ordered.</returns>
    private static List<Guid> RunningIdsOf(JsonElement payload)
    {
        var ids = new List<Guid>();
        foreach (var id in payload.GetProperty("ids").EnumerateArray())
        {
            ids.Add(id.GetGuid());
        }

        return ids.Order().ToList();
    }

    /// <summary>Migrates, seeds two tenants, their logins, an administrator and four running tasks.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The ids of the four tasks.</returns>
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

        return new SeededTasks(
            await BeginAsync(factory, TaskKinds.BackupCreate, "acme", ownAccountId, TaskAudience.AccountOwner),
            await BeginAsync(factory, TaskKinds.BackupRestore, "acme", ownAccountId, TaskAudience.AccountOwner),
            await BeginAsync(factory, TaskKinds.BackupCreate, "other", otherAccountId, TaskAudience.AccountOwner),
            await BeginAsync(
                factory, TaskKinds.AccountSuspension, "acme", ownAccountId, TaskAudience.AdministratorOnly));
    }

    /// <summary>Records one running task through the panel's recorder, with a log line on it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="kind">The task's kind.</param>
    /// <param name="subject">The task's subject.</param>
    /// <param name="accountId">The account it concerns.</param>
    /// <param name="audience">Who besides an administrator may read it.</param>
    /// <returns>The task's id.</returns>
    private static async Task<Guid> BeginAsync(
        WebApplicationFactory<Program> factory,
        string kind,
        string subject,
        Guid accountId,
        TaskAudience audience)
    {
        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ITaskRecorder>();

        var id = await recorder.BeginAsync(kind, subject, accountId, audience, correlationId: null, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, id);
        await recorder.ReportAsync(id, 10, "operator-only line", CancellationToken.None);
        return id;
    }

    /// <summary>Reads one complete server-sent event, requiring the blank line that terminates it.</summary>
    /// <param name="reader">The response body reader.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The event's name and its parsed payload.</returns>
    private static async Task<SseEvent> ReadEventAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var name = string.Empty;
        while (true)
        {
            var line = await ReadLineAsync(reader, cancellationToken);
            if (line is null)
            {
                Assert.Fail("The stream ended before the expected event arrived.");
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
                continue;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                // A comment (the opening frame, a heartbeat) or a blank line between events.
                continue;
            }

            using var payload = JsonDocument.Parse(line["data: ".Length..]);
            var element = payload.RootElement.Clone();
            Assert.Equal(string.Empty, await ReadLineAsync(reader, cancellationToken));
            return new SseEvent(name, element);
        }
    }

    /// <summary>Reads one line, bounded by a timeout the reader itself does not honour.</summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="cancellationToken">The read deadline, for the reader that does observe it.</param>
    /// <returns>The line, or null at the end of the stream.</returns>
    private static Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        return reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(ReadTimeout, CancellationToken.None);
    }

    /// <summary>The four running tasks a test reads.</summary>
    /// <param name="OwnFirst">A backup of the signed-in customer's account.</param>
    /// <param name="OwnSecond">A restore of the signed-in customer's account.</param>
    /// <param name="OtherTenant">A backup of another tenant's account.</param>
    /// <param name="AdministratorOnly">A suspension of the customer's own account, administrator-only.</param>
    private sealed record SeededTasks(Guid OwnFirst, Guid OwnSecond, Guid OtherTenant, Guid AdministratorOnly);

    /// <summary>What a subscriber reads up to the first running set.</summary>
    /// <param name="Status">The response status.</param>
    /// <param name="TaskIds">The ids the task frames carried.</param>
    /// <param name="Logs">The logs the task frames carried.</param>
    /// <param name="RunningIds">The ids the running set named.</param>
    private sealed record Opening(
        HttpStatusCode Status,
        List<Guid> TaskIds,
        List<string> Logs,
        List<Guid> RunningIds);

    /// <summary>One server-sent event as this test reads it.</summary>
    /// <param name="Name">The event name: <c>task</c> or <c>running</c>.</param>
    /// <param name="Payload">Its JSON data.</param>
    private sealed record SseEvent(string Name, JsonElement Payload);
}
