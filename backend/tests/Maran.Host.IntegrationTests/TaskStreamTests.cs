using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel-task stream over real HTTP: <c>GET /api/v1/tasks/{id}/stream</c> as server-sent events,
/// against real PostgreSQL, driven through <c>TasksController</c>, <c>TaskStreamService</c> and
/// <c>TaskStreamWriter</c> together — the whole chain R9 describes.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the module's existing tests do not show.</b> <c>TaskStreamWriterTests</c> feeds the
/// writer a hand-built <see cref="IAsyncEnumerable{T}"/> of frames directly, and
/// <c>TaskStreamServiceTests</c> drives <c>TaskStreamService.ReadAsync</c> against an EF Core
/// context with no HTTP anywhere in the picture. Both are real and worth keeping — neither one ever
/// asks whether <c>TasksController.GetStreamAsync</c> is wired to either of them, whether the route
/// resolves, whether the authorization policy lets an admin through, or whether what a real
/// <see cref="HttpClient"/> receives over a real response body is still two events correctly split
/// on the blank line that terminates each one. A regression confined to the controller — the wrong
/// id forwarded, the write started but never awaited, the route retyped — passes every test in
/// either file while breaking the one thing an operator's browser actually does.
/// </para>
/// <para>
/// <b>Two frames, not one, and the order matters.</b> A single frame proves the connection opens; it
/// says nothing about whether a watcher already attached is told about a LATER change, which is the
/// entire reason a task is streamed instead of read once. This test seeds a running task, opens the
/// stream, asserts the first frame, then reports progress through <c>ITaskRecorder</c> — the same
/// interface every other module calls — and asserts that the second, later frame is a genuinely new
/// SSE event: its own <c>event:</c> line, its own <c>data:</c> line, and the blank line that a
/// browser's <c>EventSource</c> parser requires to dispatch it at all. Skipping that blank-line
/// assertion would let this test pass against a writer that ran two payloads together as one
/// malformed frame — accepted by a lenient JSON-first reader, rejected by every real client.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TaskStreamTests : IAsyncLifetime
{
    /// <summary>The password the seeded administrator signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The bound on every stream read, so a failure to produce a frame fails rather than hangs.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TaskStreamTests(PostgresFixture postgres)
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
    /// A watcher who attaches before any change receives the task's current state, then a second,
    /// distinct frame when it changes — each one correctly framed and in the order they happened.
    /// </summary>
    [Fact]
    public async Task A_watcher_receives_two_successive_task_frames_correctly_framed_and_in_order()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAdministratorAsync(factory);
        var taskId = await SeedRunningTaskAsync(factory);
        using var client = await SignInAsync(factory);

        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tasks/{taskId}/stream");
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var reader = new StreamReader(body);

        // The raw bytes of the opening announcement: a comment frame, terminated by the blank line
        // that makes it exactly one SSE frame. Headers do not flush before this, so it is also what
        // tells a real client "connected" before any task data exists to send.
        Assert.Equal(": open", await ReadLineAsync(reader, deadline.Token));
        Assert.Equal(string.Empty, await ReadLineAsync(reader, deadline.Token));

        var first = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("task", first.Name);
        Assert.Equal(0, first.Payload.GetProperty("percent").GetInt32());
        Assert.Equal(0, first.Payload.GetProperty("revision").GetInt32());

        // The change a watcher who is ALREADY attached must be told about. Reported through
        // ITaskRecorder — the interface every instrumented operation in the panel actually calls —
        // rather than by writing the row directly, so the write half of this test is the real
        // production path too.
        await ReportProgressAsync(factory, taskId);

        var second = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("task", second.Name);
        Assert.Equal(55, second.Payload.GetProperty("percent").GetInt32());
        Assert.Equal(1, second.Payload.GetProperty("revision").GetInt32());
        Assert.Equal("halfway there", second.Payload.GetProperty("log").GetString());

        // Closing the task ends the stream, and it takes TWO frames to do it: completing moves the
        // revision like any other change, so the watcher is sent the finished row itself and only
        // then the ending. The order is the contract `TaskStreamService` states — "one more frame
        // every time it changes, and exactly one ending" — and it is the order a pane needs, since
        // a watcher told only "it ended" would never receive the hundred percent or the final log
        // line it ended with.
        await CompleteTaskAsync(factory, taskId);

        var closing = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("task", closing.Name);
        Assert.Equal("completed", closing.Payload.GetProperty("status").GetString());
        Assert.Equal(100, closing.Payload.GetProperty("percent").GetInt32());
        Assert.Equal(2, closing.Payload.GetProperty("revision").GetInt32());

        // And then the ending itself — a separate, correctly terminated frame, so the connection
        // this test opened is torn down by the server reaching its own close rather than by a
        // forced cancellation.
        var end = await ReadEventAsync(reader, deadline.Token);
        Assert.Equal("end", end.Name);
        Assert.Equal("completed", end.Payload.GetProperty("status").GetString());
    }

    /// <summary>Boots the host against this class's PostgreSQL, with a short stream poll interval.</summary>
    /// <returns>The factory.</returns>
    /// <remarks>
    /// The poll interval is turned down from its 500 ms default so the second and third frames of
    /// the test above arrive quickly and deterministically rather than merely within the read
    /// timeout — the same reason <c>SiteLogStreamTests</c> turns its heartbeat interval down for the
    /// tests that need to observe one.
    ///
    /// It is turned down to the option's own FLOOR and no further. <c>TaskStreamOptions</c> carries
    /// <c>[Range(100, 2000)]</c> and the host validates its options on start, so a smaller value is
    /// not a faster test — it is an <c>OptionsValidationException</c> before a single line of this
    /// file runs, which is how this test first arrived red with a message about configuration and
    /// nothing at all to say about framing.
    /// </remarks>
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
            builder.UseSetting("Tasks:Stream:PollIntervalMilliseconds", "100");

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies the two modules' migrations this test's world needs.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds the one administrator this test signs in as. Tasks are admin-only (R14).</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
        await identity.SaveChangesAsync();
    }

    /// <summary>Seeds one running task directly, the state a watcher's first frame reports.</summary>
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

    /// <summary>Reports progress on the seeded task through the panel-wide recorder.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="taskId">The task to report against.</param>
    private static async Task ReportProgressAsync(WebApplicationFactory<Program> factory, Guid taskId)
    {
        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ITaskRecorder>();
        await recorder.ReportAsync(taskId, 55, "halfway there", CancellationToken.None);
    }

    /// <summary>Completes the seeded task through the panel-wide recorder.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="taskId">The task to complete.</param>
    private static async Task CompleteTaskAsync(WebApplicationFactory<Program> factory, Guid taskId)
    {
        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ITaskRecorder>();
        await recorder.CompleteAsync(taskId, CancellationToken.None);
    }

    /// <summary>Signs the seeded administrator in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The signed-in client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = "admin", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Reads one complete server-sent event, requiring the blank line that terminates it.</summary>
    /// <param name="reader">The response body reader.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The event's name and its parsed payload.</returns>
    /// <remarks>
    /// This is the assertion that makes the test about FRAMING and not only about content. A frame's
    /// <c>data:</c> line is read, and then the very next line is required to be empty — the boundary
    /// a browser's server-sent-events parser (and the SPA's own stream helper)
    /// relies on to know one event has ended and decide whether another begins. A writer that folded
    /// two updates into one frame, or dropped the separating blank line, fails HERE rather than
    /// producing a payload this method would still happily parse.
    /// </remarks>
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

            if (line.StartsWith(':'))
            {
                // A heartbeat, or the opening comment already consumed above. No event, and — like
                // a browser's own parser — nothing is dispatched for it.
                continue;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using var payload = JsonDocument.Parse(line["data: ".Length..]);
            var snapshot = payload.RootElement.Clone();

            Assert.Equal(string.Empty, await ReadLineAsync(reader, cancellationToken));

            return new SseEvent(name, snapshot);
        }
    }

    /// <summary>Reads one line, bounded by a timeout the reader itself does not honour.</summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="cancellationToken">The read deadline, for the reader that does observe it.</param>
    /// <returns>The line, or null at the end of the stream.</returns>
    /// <remarks>
    /// A read on a response stream that never produces another byte does not observe its
    /// cancellation token here, so the token by itself is a bound that does not bind — the same
    /// reason <c>SiteLogStreamTests</c> wraps every one of its own reads in <c>WaitAsync</c>.
    /// </remarks>
    private static Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        return reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(ReadTimeout, CancellationToken.None);
    }

    /// <summary>One server-sent event as this test reads it.</summary>
    /// <param name="Name">The event name: <c>task</c> or <c>end</c>.</param>
    /// <param name="Payload">Its JSON data.</param>
    private sealed record SseEvent(string Name, JsonElement Payload);
}
