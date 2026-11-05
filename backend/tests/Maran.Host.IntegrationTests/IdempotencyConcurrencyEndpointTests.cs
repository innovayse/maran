using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Two requests carrying one idempotency key at the same instant (issue #136): exactly one runs, and the
/// other is either told the key is in progress or answered from the store, never allowed to run the
/// action a second time. The unique index on caller and key is what arbitrates; these tests are the
/// observation that it does.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class IdempotencyConcurrencyEndpointTests : IAsyncLifetime
{
    /// <summary>How long a test waits for something it has arranged to happen before it fails.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public IdempotencyConcurrencyEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// While the first creation is held inside the agent, a second request with the same key is refused
    /// with 409 <c>IdempotencyRequestInProgress</c>; once the first finishes, a third is a replay. The
    /// agent saw one creation throughout. The hold is what puts the first request provably in flight at
    /// the instant the second is sent.
    /// </summary>
    [Fact]
    public async Task A_second_request_whose_first_is_still_running_is_refused_as_in_progress_and_later_replays()
    {
        var agent = new ScriptedAccountsAgent();
        agent.HoldCreations();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var body = IdempotencyHost.CreateBody("acme", planId);

        var running = admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));
        await agent.CreationEntered.WaitAsync(Patience);
        using var duringRun = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));
        agent.ReleaseCreations();
        using var first = await running.WaitAsync(Patience);
        using var afterRun = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));

        Assert.Equal(HttpStatusCode.Conflict, duringRun.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.RequestInProgress, await ProblemDocument.CodeAsync(duringRun));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterRun.StatusCode);
        Assert.True(afterRun.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["acme"], agent.Created);
        Assert.Equal(1, await IdempotencyHost.StoredAccountsAsync(factory, "acme"));
    }

    /// <summary>
    /// Two requests released together by a start gate, sharing one key, run the creation once: one answer
    /// is a fresh 201, the other is 409 <c>IdempotencyRequestInProgress</c> or a replay of that 201. Which
    /// of the two the loser sees depends on the scheduler, and both are correct; running twice is not.
    /// </summary>
    [Fact]
    public async Task Two_simultaneous_requests_with_one_key_run_the_creation_once()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var first = await EndpointHarness.SignInAsync(factory, "admin");
        using var second = await EndpointHarness.SignInAsync(factory, "admin");
        var body = IdempotencyHost.CreateBody("acme", planId);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var one = SendWhenReleasedAsync(first, start.Task, body);
        var other = SendWhenReleasedAsync(second, start.Task, body);
        start.SetResult();
        var answers = await Task.WhenAll(one, other).WaitAsync(Patience);

        var fresh = answers.Where(answer =>
        {
            return answer.Status == HttpStatusCode.Created && !answer.Replayed;
        }).ToList();
        var others = answers.Where(answer =>
        {
            return answer.Status != HttpStatusCode.Created || answer.Replayed;
        }).ToList();
        Assert.Single(fresh);
        var loser = Assert.Single(others);
        Assert.True(
            loser is { Status: HttpStatusCode.Created, Replayed: true }
                || loser is { Status: HttpStatusCode.Conflict, Code: IdempotencyErrorCodes.RequestInProgress },
            $"The second request answered {(int)loser.Status} {loser.Code}, replayed: {loser.Replayed}.");
        Assert.Equal(["acme"], agent.Created);
        Assert.Equal(1, await IdempotencyHost.StoredAccountsAsync(factory, "acme"));
        Assert.Single(await IdempotencyHost.StoredKeysAsync(factory));
    }

    /// <summary>Sends the creation once the start gate opens, and reads what it answered.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="gate">Completes when both requests may go.</param>
    /// <param name="body">The creation body.</param>
    /// <returns>The status, whether the answer was a replay, and the refusal code if there is one.</returns>
    private static async Task<(HttpStatusCode Status, bool Replayed, string? Code)> SendWhenReleasedAsync(
        HttpClient client,
        Task gate,
        object body)
    {
        await gate;
        using var response = await client.SendAsync(
            IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));
        var code = response.IsSuccessStatusCode ? null : await ProblemDocument.CodeAsync(response);

        return (response.StatusCode, response.Headers.Contains(IdempotencyHeaders.Replayed), code);
    }
}
