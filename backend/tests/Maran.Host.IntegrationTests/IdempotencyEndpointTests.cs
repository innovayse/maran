using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Idempotency keys over real HTTP against a real PostgreSQL (issue #136): the account endpoints tagged
/// <c>[Idempotent]</c> run once for a key and answer a repeat from the store; a key reused for another
/// request, a malformed key, a server failure and an expired record are each decided as documented, and
/// two callers who pick the same key text never see one another's answers. The agent double records
/// every call that reached it, because a replay is only proven when the agent saw the work once.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class IdempotencyEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public IdempotencyEndpointTests(PostgresFixture postgres)
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
    /// Sending the same creation twice with one key creates one account, reaches the agent once, and
    /// answers the second request with the first status and body marked <c>Idempotent-Replayed: true</c>.
    /// </summary>
    [Fact]
    public async Task Repeating_a_creation_with_the_same_key_creates_one_account_and_replays_the_first_answer()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var body = IdempotencyHost.CreateBody("acme", planId);

        using var first = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));
        using var second = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(IdempotencyHeaders.Replayed)));
        Assert.Equal(await IdOfAsync(first), await IdOfAsync(second));
        Assert.Equal(["acme"], agent.Created);
        Assert.Equal(1, await IdempotencyHost.StoredAccountsAsync(factory, "acme"));
    }

    /// <summary>
    /// A key used again with a different body is refused with 422 <c>IdempotencyKeyReused</c>, and the
    /// second body reaches neither the agent nor the database.
    /// </summary>
    [Fact]
    public async Task Reusing_a_key_with_a_different_body_is_refused_with_a_code_and_runs_nothing()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var first = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "key-1", IdempotencyHost.CreateBody("acme", planId)));
        using var second = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "key-1", IdempotencyHost.CreateBody("beta", planId)));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyReused, await ProblemDocument.CodeAsync(second));
        Assert.False(second.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["acme"], agent.Created);
        Assert.Equal(0, await IdempotencyHost.StoredAccountsAsync(factory, "beta"));
    }

    /// <summary>
    /// Without the header nothing changes: the second identical creation is refused as a taken name,
    /// as it always was, and no idempotency record is written for either request.
    /// </summary>
    [Fact]
    public async Task Two_creations_without_a_key_behave_as_before_and_store_no_record()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var body = IdempotencyHost.CreateBody("acme", planId);

        using var first = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", null, body));
        using var second = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", null, body));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("AccountNameTaken", await ProblemDocument.CodeAsync(second));
        Assert.False(second.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["acme"], agent.Created);
        Assert.Empty(await IdempotencyHost.StoredKeysAsync(factory));
    }

    /// <summary>
    /// A key that has a character outside <c>[A-Za-z0-9_:.-]</c>, or is longer than 255
    /// characters is refused with 400 <c>IdempotencyKeyInvalid</c> and the request never runs.
    /// </summary>
    /// <param name="key">A key the filter must refuse.</param>
    [Theory]
    [InlineData("bad key")]
    [InlineData("bad!key")]
    [InlineData("bad/key")]
    public async Task A_malformed_key_is_refused_with_a_code_and_the_request_never_runs(string key)
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", key, IdempotencyHost.CreateBody("acme", planId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyInvalid, await ProblemDocument.CodeAsync(response));
        Assert.Empty(agent.Created);
        Assert.Empty(await IdempotencyHost.StoredKeysAsync(factory));
    }

    /// <summary>A key of 256 characters is one past the limit and is refused like any other malformed key.</summary>
    [Fact]
    public async Task A_key_one_character_over_the_length_limit_is_refused()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post,
            "/api/v1/accounts",
            new string('a', IdempotencyLimits.MaxKeyLength + 1),
            IdempotencyHost.CreateBody("acme", planId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyInvalid, await ProblemDocument.CodeAsync(response));
        Assert.Empty(agent.Created);
    }

    /// <summary>
    /// The inverse control of the refusals: a key using the whole allowed alphabet, and a one-character
    /// key, are accepted and stored as sent. Without it the refusals above could pass over a filter
    /// that refuses every key.
    /// </summary>
    /// <param name="key">A key at the edge of what is acceptable.</param>
    [Theory]
    [InlineData("aZ09_:.-")]
    [InlineData("k")]
    public async Task A_key_using_the_allowed_alphabet_is_accepted_and_the_request_runs(string key)
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", key, IdempotencyHost.CreateBody("acme", planId)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["acme"], agent.Created);
        Assert.Equal(key, Assert.Single(await IdempotencyHost.StoredKeysAsync(factory)).Key);
    }

    /// <summary>The longest allowed key, 255 characters, is accepted.</summary>
    [Fact]
    public async Task A_key_of_exactly_the_longest_allowed_length_is_accepted()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post,
            "/api/v1/accounts",
            new string('a', IdempotencyLimits.MaxKeyLength),
            IdempotencyHost.CreateBody("acme", planId)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["acme"], agent.Created);
    }

    /// <summary>
    /// A server failure is not stored: the reservation is released, so a retry with the same key runs the
    /// work again instead of being answered with the failure for a day.
    /// </summary>
    [Fact]
    public async Task A_server_failure_is_not_stored_and_a_retry_with_the_same_key_runs_again()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var body = IdempotencyHost.CreateBody("acme", planId);
        agent.FailNextCreation();

        using var failed = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));
        var keptAfterFailure = await IdempotencyHost.StoredKeysAsync(factory);
        using var retry = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "key-1", body));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(keptAfterFailure);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(retry.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["acme", "acme"], agent.Created);
        Assert.Equal(1, await IdempotencyHost.StoredAccountsAsync(factory, "acme"));
    }

    /// <summary>
    /// A deterministic refusal (4xx) IS stored: the same key and request is answered with the same
    /// refusal, replayed, and the second attempt does not reach the handler's checks again.
    /// </summary>
    [Fact]
    public async Task A_client_error_is_stored_and_replayed_for_the_same_key()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var missing = Guid.NewGuid();

        using var first = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, $"/api/v1/accounts/{missing}/suspend", "key-1"));
        using var second = await admin.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, $"/api/v1/accounts/{missing}/suspend", "key-1"));

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("AccountNotFound", await ProblemDocument.CodeAsync(second));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(IdempotencyHeaders.Replayed)));
        Assert.Empty(agent.Suspended);
    }

    /// <summary>
    /// Two administrators who send the same key text hold two unrelated keys: the second creates their own
    /// account, and is never answered with the first administrator's stored response.
    /// </summary>
    [Fact]
    public async Task Two_administrators_using_the_same_key_text_are_independent()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var first = await EndpointHarness.SignInAsync(factory, "admin");
        using var second = await EndpointHarness.SignInAsync(factory, IdempotencyHost.SecondAdministrator);

        using var byFirst = await first.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "shared-key", IdempotencyHost.CreateBody("acme", planId)));
        using var bySecond = await second.SendAsync(IdempotencyHost.Request(
            HttpMethod.Post, "/api/v1/accounts", "shared-key", IdempotencyHost.CreateBody("beta", planId)));

        Assert.Equal(HttpStatusCode.Created, byFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bySecond.StatusCode);
        Assert.False(bySecond.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["acme", "beta"], agent.Created);
        Assert.Equal(2, (await IdempotencyHost.StoredKeysAsync(factory)).Count);
    }

    /// <summary>
    /// Another administrator sending the very same request with the same key is not given the first
    /// administrator's answer: it runs as their own request and meets the real conflict.
    /// </summary>
    [Fact]
    public async Task A_key_one_administrator_used_is_never_replayed_to_another()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, planId) = await EndpointHarness.SeedAccountAsync(factory, "seeded");
        using var first = await EndpointHarness.SignInAsync(factory, "admin");
        using var second = await EndpointHarness.SignInAsync(factory, IdempotencyHost.SecondAdministrator);
        var body = IdempotencyHost.CreateBody("acme", planId);

        using var byFirst = await first.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "shared-key", body));
        using var bySecond = await second.SendAsync(IdempotencyHost.Request(HttpMethod.Post, "/api/v1/accounts", "shared-key", body));

        Assert.Equal(HttpStatusCode.Created, byFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, bySecond.StatusCode);
        Assert.Equal("AccountNameTaken", await ProblemDocument.CodeAsync(bySecond));
        Assert.False(bySecond.Headers.Contains(IdempotencyHeaders.Replayed));
    }

    /// <summary>
    /// A record past its expiry is treated as absent: the same key and request, repeated after the
    /// completed window, runs again instead of replaying, and the record is rewritten at the new time.
    /// The pre-expiry repeat is the control that shows the replay was there to be lost.
    /// </summary>
    [Fact]
    public async Task An_expired_record_is_treated_as_absent_and_the_request_runs_again()
    {
        var clock = new AdvancingClock();
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent, clock);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        var small = await IdempotencyHost.SeedSmallPlanAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var url = $"/api/v1/accounts/{accountId}/plan";

        using var first = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Put, url, "key-1", new { PlanId = small }));
        using var beforeExpiry = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Put, url, "key-1", new { PlanId = small }));
        clock.Advance(TimeSpan.FromHours(25));
        using var afterExpiry = await admin.SendAsync(IdempotencyHost.Request(HttpMethod.Put, url, "key-1", new { PlanId = small }));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(beforeExpiry.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.False(afterExpiry.Headers.Contains(IdempotencyHeaders.Replayed));
        var kept = Assert.Single(await IdempotencyHost.StoredKeysAsync(factory));
        Assert.Equal(clock.UtcNow, kept.CreatedAt);
    }

    /// <summary>
    /// The suspend, reactivate and plan-change endpoints each run once for a key: the repeat is a replay
    /// and the agent saw one call of each kind.
    /// </summary>
    [Fact]
    public async Task Suspending_reactivating_and_changing_a_plan_each_run_once_for_a_key()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        var small = await IdempotencyHost.SeedSmallPlanAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var suspend = await TwiceAsync(admin, HttpMethod.Post, $"/api/v1/accounts/{accountId}/suspend", "suspend-1");
        var reactivate = await TwiceAsync(admin, HttpMethod.Post, $"/api/v1/accounts/{accountId}/reactivate", "reactivate-1");
        var plan = await TwiceAsync(admin, HttpMethod.Put, $"/api/v1/accounts/{accountId}/plan", "plan-1", new { PlanId = small });

        Assert.All([suspend, reactivate, plan], pair =>
        {
            Assert.Equal(HttpStatusCode.OK, pair.First);
            Assert.Equal(HttpStatusCode.OK, pair.Second);
            Assert.True(pair.SecondReplayed);
        });
        Assert.Equal(["olive"], agent.Suspended);
        Assert.Equal(["olive"], agent.Unsuspended);
        Assert.Single(agent.Quotas);
    }

    /// <summary>A repeated deletion with one key deletes the account once and replays the answer.</summary>
    [Fact]
    public async Task Deleting_an_account_twice_with_one_key_deletes_it_once()
    {
        var agent = new ScriptedAccountsAgent();
        await using var factory = IdempotencyHost.CreateFactory(_pg, agent);
        await IdempotencyHost.PrepareAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var deletion = await TwiceAsync(admin, HttpMethod.Delete, $"/api/v1/accounts/{accountId}", "delete-1");

        Assert.Equal(HttpStatusCode.OK, deletion.First);
        Assert.Equal(HttpStatusCode.OK, deletion.Second);
        Assert.True(deletion.SecondReplayed);
        Assert.Equal(["olive"], agent.Deleted);
        Assert.Equal(0, await IdempotencyHost.StoredAccountsAsync(factory, "olive"));
    }

    /// <summary>Sends one request twice with one key and reports both statuses and whether the second was a replay.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The request path.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="body">The JSON body, or null.</param>
    /// <returns>The two statuses and the replay flag of the second answer.</returns>
    private static async Task<(HttpStatusCode First, HttpStatusCode Second, bool SecondReplayed)> TwiceAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string key,
        object? body = null)
    {
        using var first = await client.SendAsync(IdempotencyHost.Request(method, url, key, body));
        using var second = await client.SendAsync(IdempotencyHost.Request(method, url, key, body));

        return (first.StatusCode, second.StatusCode, second.Headers.Contains(IdempotencyHeaders.Replayed));
    }

    /// <summary>Reads the <c>id</c> of a created account out of a response.</summary>
    /// <param name="response">The creation response.</param>
    /// <returns>The account's id.</returns>
    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }
}
