using System.Security.Cryptography;
using System.Text;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;
using OptionsWrapper = Microsoft.Extensions.Options.OptionsWrapper<Microsoft.AspNetCore.Mvc.JsonOptions>;

namespace Maran.Sdk.Tests.Filters;

/// <summary>
/// Behavioral contract of <see cref="IdempotencyFilter"/>: a request without a key runs as before, a
/// new key is reserved and its answer stored, an identical repeat is replayed, a reused key and a
/// running key are refused, nothing but a deterministic answer is kept, and a no-store action never has
/// its body stored.
/// </summary>
public sealed class IdempotencyFilterTests
{
    /// <summary>The panel login the requests run as.</summary>
    private static readonly Guid UserId = Guid.Parse("44444444-0000-4000-8000-000000000001");

    /// <summary>Two values of the key header, which makes the header ambiguous.</summary>
    private static readonly string[] RepeatedKeyValues = ["a", "b"];

    /// <summary>A request with no <c>Idempotency-Key</c> runs the action and never touches the store.</summary>
    [Fact]
    public async Task A_request_without_a_key_runs_as_before_and_never_touches_the_store()
    {
        var run = await RunAsync(new Scenario { Key = null });

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
        Assert.Empty(run.Store.Begun);
        Assert.Empty(run.Store.Completed);
    }

    /// <summary>A new key is reserved for the caller, the action runs, and its 2xx answer is stored with its serialized body.</summary>
    [Fact]
    public async Task A_new_key_runs_the_action_and_stores_its_answer()
    {
        var run = await RunAsync(new Scenario { Result = new OkObjectResult(new Reply("done")) });

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
        var begun = Assert.Single(run.Store.Begun);
        Assert.Equal(($"user:{UserId:D}", "key-1"), (begun.CallerKey, begun.Key));
        var completed = Assert.Single(run.Store.Completed);
        Assert.Equal(StatusCodes.Status200OK, completed.StatusCode);
        Assert.Equal("application/json", completed.ContentType);
        Assert.Equal("{\"name\":\"done\"}", completed.Body);
        Assert.Empty(run.Store.Abandoned);
    }

    /// <summary>A 4xx problem answer is deterministic and is stored too, as problem JSON.</summary>
    [Fact]
    public async Task A_client_error_answer_is_stored_as_problem_json()
    {
        var problem = new ObjectResult(new ProblemDetails { Status = 409, Title = "taken" }) { StatusCode = 409 };

        var run = await RunAsync(new Scenario { Result = problem });

        var completed = Assert.Single(run.Store.Completed);
        Assert.Equal(StatusCodes.Status409Conflict, completed.StatusCode);
        Assert.Equal("application/problem+json", completed.ContentType);
    }

    /// <summary>A status-code answer such as 204 is stored with no body.</summary>
    [Fact]
    public async Task A_bodyless_answer_is_stored_with_no_body()
    {
        var run = await RunAsync(new Scenario { Result = new NoContentResult() });

        var completed = Assert.Single(run.Store.Completed);
        Assert.Equal(StatusCodes.Status204NoContent, completed.StatusCode);
        Assert.Null(completed.Body);
    }

    /// <summary>An identical finished request is answered from the store, the action never runs, and the replay is marked.</summary>
    [Fact]
    public async Task A_replay_answers_the_stored_status_body_and_type_without_running_the_action()
    {
        var store = new FakeStore { Answer = new IdempotencyReplay(201, "application/json", "{\"id\":1}") };

        var run = await RunAsync(new Scenario { Store = store });

        Assert.False(run.ActionRan);
        var content = Assert.IsType<ContentResult>(run.Context.Result);
        Assert.Equal(201, content.StatusCode);
        Assert.Equal("{\"id\":1}", content.Content);
        Assert.Equal("application/json", content.ContentType);
        Assert.Equal("true", run.Http.Response.Headers[IdempotencyHeaders.Replayed].ToString());
        Assert.Empty(run.Store.Completed);
    }

    /// <summary>A key used by a different request is refused with 422 and its code.</summary>
    [Fact]
    public async Task A_reused_key_is_refused_with_422()
    {
        var run = await RunAsync(new Scenario { Store = new FakeStore { Answer = new IdempotencyMismatch() } });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status422UnprocessableEntity, "IdempotencyKeyReused");
    }

    /// <summary>A key whose first request is still running is refused with 409 and its code.</summary>
    [Fact]
    public async Task A_key_still_in_progress_is_refused_with_409()
    {
        var run = await RunAsync(new Scenario { Store = new FakeStore { Answer = new IdempotencyInProgress() } });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status409Conflict, "IdempotencyRequestInProgress");
    }

    /// <summary>A 5xx answer is never stored and the reservation is released so the caller can retry.</summary>
    [Fact]
    public async Task A_server_error_answer_is_not_stored_and_the_key_is_abandoned()
    {
        var run = await RunAsync(new Scenario { Result = new ObjectResult(new ProblemDetails()) { StatusCode = 503 } });

        Assert.Empty(run.Store.Completed);
        Assert.Equal(($"user:{UserId:D}", "key-1"), Assert.Single(run.Store.Abandoned));
    }

    /// <summary>An answer about the caller's credentials or the moment (401, 403, 408, 429) is never stored.</summary>
    /// <param name="status">The status the action answered with.</param>
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    public async Task A_credential_or_timing_refusal_is_not_stored_and_the_key_is_abandoned(int status)
    {
        var run = await RunAsync(new Scenario { Result = new ObjectResult(new ProblemDetails()) { StatusCode = status } });

        Assert.Empty(run.Store.Completed);
        Assert.Single(run.Store.Abandoned);
    }

    /// <summary>An action that throws releases the reservation and the exception still propagates.</summary>
    [Fact]
    public async Task An_action_that_throws_abandons_the_key()
    {
        var store = new FakeStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return RunAsync(new Scenario { Store = store, Throws = true });
        });

        Assert.Single(store.Abandoned);
        Assert.Empty(store.Completed);
    }

    /// <summary>An answer larger than the stored-body cap is not stored and the key is released.</summary>
    [Fact]
    public async Task An_oversized_answer_is_not_stored_and_the_key_is_abandoned()
    {
        var big = new string('x', IdempotencyLimits.MaxStoredBodyBytes + 1);

        var run = await RunAsync(new Scenario { Result = new OkObjectResult(new Reply(big)) });

        Assert.Empty(run.Store.Completed);
        Assert.Single(run.Store.Abandoned);
    }

    /// <summary>A no-store action keeps the key and its status but never the body or content type.</summary>
    [Fact]
    public async Task A_no_store_action_completes_the_key_without_a_body()
    {
        var run = await RunAsync(new Scenario { Result = new OkObjectResult(new Reply("secret")), NoStore = true });

        var completed = Assert.Single(run.Store.Completed);
        Assert.Equal(StatusCodes.Status200OK, completed.StatusCode);
        Assert.Null(completed.Body);
        Assert.Null(completed.ContentType);
    }

    /// <summary>A replay of a no-store action is refused with 409 and its code, never answered.</summary>
    [Fact]
    public async Task A_replay_of_a_no_store_action_is_refused_with_409()
    {
        var store = new FakeStore { Answer = new IdempotencyReplay(200, null, null) };

        var run = await RunAsync(new Scenario { Store = store, NoStore = true });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status409Conflict, "IdempotencyResponseNotStored");
    }

    /// <summary>Keys that are empty, too long, or outside the charset are refused with 400 and the store is never asked.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/not/allowed")]
    [InlineData("unicode-é")]
    public async Task A_malformed_key_is_refused_with_400(string key)
    {
        var run = await RunAsync(new Scenario { Key = key });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
        Assert.Empty(run.Store.Begun);
    }

    /// <summary>A 256-character key is refused and a 255-character one is accepted.</summary>
    [Fact]
    public async Task A_key_is_at_most_255_characters()
    {
        var tooLong = await RunAsync(new Scenario { Key = new string('a', 256) });
        var longest = await RunAsync(new Scenario { Key = new string('a', 255) });

        AssertProblem(tooLong, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
        Assert.True(longest.ActionRan);
    }

    /// <summary>Every character of the allowed set is accepted.</summary>
    [Fact]
    public async Task The_whole_allowed_charset_is_accepted()
    {
        var run = await RunAsync(new Scenario { Key = "aZ09_:.-" });

        Assert.True(run.ActionRan);
        Assert.Single(run.Store.Begun);
    }

    /// <summary>A repeated header is ambiguous and refused with 400.</summary>
    [Fact]
    public async Task A_repeated_key_header_is_refused_with_400()
    {
        var run = await RunAsync(new Scenario { KeyValues = new StringValues(RepeatedKeyValues) });

        AssertProblem(run, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
    }

    /// <summary>A body larger than 1 MiB, declared by its length, is refused with 400 and the store is never asked.</summary>
    [Fact]
    public async Task An_oversized_body_with_a_declared_length_is_refused_with_400()
    {
        var run = await RunAsync(new Scenario { Body = new byte[IdempotencyLimits.MaxRequestBodyBytes + 1] });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
        Assert.Empty(run.Store.Begun);
    }

    /// <summary>A body larger than 1 MiB with no declared length (chunked) is refused too, after reading no more than the cap plus one byte.</summary>
    [Fact]
    public async Task An_oversized_chunked_body_is_refused_with_400()
    {
        var run = await RunAsync(new Scenario { Body = new byte[IdempotencyLimits.MaxRequestBodyBytes + 1], Chunked = true });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
        Assert.Empty(run.Store.Begun);
    }

    /// <summary>A chunked body several KiB past the cap, which trips the buffering limit, is refused with 400 and not a 500.</summary>
    [Fact]
    public async Task A_chunked_body_far_past_the_cap_is_refused_with_400()
    {
        var run = await RunAsync(new Scenario { Body = new byte[IdempotencyLimits.MaxRequestBodyBytes + 20000], Chunked = true });

        Assert.False(run.ActionRan);
        AssertProblem(run, StatusCodes.Status400BadRequest, "IdempotencyKeyInvalid");
    }

    /// <summary>A body of exactly 1 MiB is accepted, and the action can still read it afterwards.</summary>
    [Fact]
    public async Task A_body_of_exactly_the_cap_is_accepted_and_left_readable()
    {
        var body = new byte[IdempotencyLimits.MaxRequestBodyBytes];

        var run = await RunAsync(new Scenario { Body = body });

        Assert.True(run.ActionRan);
        Assert.Equal(0, run.Http.Request.Body.Position);
    }

    /// <summary>The fingerprint follows the body: equal bodies give one hash, different bodies give two.</summary>
    [Fact]
    public async Task The_request_hash_follows_the_body()
    {
        var first = await RunAsync(new Scenario { Body = Encoding.UTF8.GetBytes("{\"a\":1}") });
        var same = await RunAsync(new Scenario { Body = Encoding.UTF8.GetBytes("{\"a\":1}") });
        var other = await RunAsync(new Scenario { Body = Encoding.UTF8.GetBytes("{\"a\":2}") });

        Assert.Equal(first.Store.Begun[0].RequestHash, same.Store.Begun[0].RequestHash);
        Assert.NotEqual(first.Store.Begun[0].RequestHash, other.Store.Begun[0].RequestHash);
        Assert.Equal(64, first.Store.Begun[0].RequestHash.Length);
    }

    /// <summary>The fingerprint follows the method and the route values, so one key cannot be replayed onto another resource.</summary>
    [Fact]
    public async Task The_request_hash_follows_the_method_and_the_route_values()
    {
        var baseline = await RunAsync(new Scenario { RouteId = "1" });
        var otherRoute = await RunAsync(new Scenario { RouteId = "2" });
        var otherMethod = await RunAsync(new Scenario { RouteId = "1", Method = "DELETE" });

        Assert.NotEqual(baseline.Store.Begun[0].RequestHash, otherRoute.Store.Begun[0].RequestHash);
        Assert.NotEqual(baseline.Store.Begun[0].RequestHash, otherMethod.Store.Begun[0].RequestHash);
    }

    /// <summary>The key is scoped to the login: two logins sending one key text reach the store under two caller keys.</summary>
    [Fact]
    public async Task The_caller_key_is_scoped_to_the_login()
    {
        var other = Guid.Parse("44444444-0000-4000-8000-000000000002");

        var first = await RunAsync(new Scenario { Caller = new AdministratorCaller(UserId, "root") });
        var second = await RunAsync(new Scenario { Caller = new AdministratorCaller(other, "other") });

        Assert.Equal($"user:{UserId:D}", first.Store.Begun[0].CallerKey);
        Assert.Equal($"user:{other:D}", second.Store.Begun[0].CallerKey);
        Assert.Equal(first.Store.Begun[0].Key, second.Store.Begun[0].Key);
    }

    /// <summary>An anonymous caller has no identity to scope a key to, so its request passes through with no idempotency.</summary>
    [Fact]
    public async Task An_anonymous_caller_passes_through_without_idempotency()
    {
        var run = await RunAsync(new Scenario { Caller = AnonymousCaller.Instance });

        Assert.True(run.ActionRan);
        Assert.Empty(run.Store.Begun);
    }

    /// <summary>With no store in the container the request runs exactly as if no key had been sent, never as a failure.</summary>
    [Fact]
    public async Task A_missing_store_passes_the_request_through_instead_of_failing()
    {
        var run = await RunAsync(new Scenario { StoreNotRegistered = true });

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
        Assert.Empty(run.Store.Begun);
        Assert.Empty(run.Store.Completed);
        Assert.False(run.Http.Response.Headers.ContainsKey(IdempotencyHeaders.Replayed));
    }

    /// <summary>With no fingerprint service in the container the request runs exactly as if no key had been sent.</summary>
    [Fact]
    public async Task A_missing_fingerprint_service_passes_the_request_through_instead_of_failing()
    {
        var run = await RunAsync(new Scenario { FingerprintNotRegistered = true });

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
        Assert.Empty(run.Store.Begun);
        Assert.Empty(run.Store.Completed);
        Assert.False(run.Http.Response.Headers.ContainsKey(IdempotencyHeaders.Replayed));
    }

    /// <summary>The token the store minted at reservation is the one handed back to complete the row.</summary>
    [Fact]
    public async Task The_reservation_token_is_passed_back_when_the_answer_is_stored()
    {
        var run = await RunAsync(new Scenario());

        Assert.Equal(FakeStore.StartedToken, Assert.Single(run.Store.CompletedTokens));
        Assert.Empty(run.Store.AbandonedTokens);
    }

    /// <summary>The token the store minted at reservation is the one handed back to release the row.</summary>
    [Fact]
    public async Task The_reservation_token_is_passed_back_when_the_reservation_is_released()
    {
        var run = await RunAsync(new Scenario { Result = new ObjectResult(new ProblemDetails()) { StatusCode = 503 } });

        Assert.Equal(FakeStore.StartedToken, Assert.Single(run.Store.AbandonedTokens));
        Assert.Empty(run.Store.CompletedTokens);
    }

    /// <summary>A store that fails after the action succeeded never turns the response into an error; the failure is logged once, without the key.</summary>
    [Fact]
    public async Task A_failure_to_store_the_answer_leaves_the_action_response_alone_and_is_logged_once_without_the_key()
    {
        var result = new OkObjectResult(new Reply("done"));

        var run = await RunAsync(new Scenario { Store = new FakeStore { CompleteThrows = true }, Result = result, Key = "secret-key-123" });

        Assert.True(run.ActionRan);
        Assert.Null(run.Context.Result);
        Assert.Empty(run.Store.Completed);
        Assert.Empty(run.Store.Abandoned);
        var entry = Assert.Single(run.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain("secret-key-123", entry.Message);
        Assert.DoesNotContain("the database is down", entry.Message);
    }

    /// <summary>An API-key caller's key is scoped to <c>key:&lt;id&gt;</c>, never to a user id.</summary>
    [Fact]
    public async Task An_api_key_caller_is_scoped_to_its_key_id()
    {
        var keyId = Guid.Parse("55555555-0000-4000-8000-000000000001");

        var run = await RunAsync(new Scenario { Caller = new ApiKeyCaller(keyId, ["self.read"]) });

        Assert.True(run.ActionRan);
        Assert.Equal($"key:{keyId:N}", Assert.Single(run.Store.Begun).CallerKey);
    }

    /// <summary>The attribute is a filter factory over <see cref="IdempotencyFilter"/>.</summary>
    [Fact]
    public void The_attribute_builds_the_idempotency_filter()
    {
        Assert.Equal(typeof(IdempotencyFilter), new IdempotentAttribute().ImplementationType);
    }

    /// <summary>Asserts the filter short-circuited with a coded problem of the given status.</summary>
    /// <param name="run">What the run produced.</param>
    /// <param name="status">The expected HTTP status.</param>
    /// <param name="code">The expected machine code.</param>
    private static void AssertProblem(FilterRun run, int status, string code)
    {
        var refusal = Assert.IsType<ObjectResult>(run.Context.Result);
        Assert.Equal(status, refusal.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(refusal.Value);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    /// <summary>Runs both filter stages the way the pipeline does: resource stage, action, result stage.</summary>
    /// <param name="scenario">What the request and the action look like.</param>
    /// <returns>What happened.</returns>
    private static async Task<FilterRun> RunAsync(Scenario scenario)
    {
        var store = scenario.Store ?? new FakeStore();
        var logger = new CapturingLogger();
        var filter = new IdempotencyFilter(new FakeUser(scenario.Caller ?? new AdministratorCaller(UserId, "root")),
            new OptionsWrapper(new MvcJsonOptions()), logger);
        var services = new ServiceCollection();

        if (!scenario.StoreNotRegistered)
        {
            services.AddSingleton<IIdempotencyStore>(store);
        }

        if (!scenario.FingerprintNotRegistered)
        {
            services.AddSingleton<IIdempotencyFingerprint>(new FakeFingerprint());
        }

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Method = scenario.Method;

        if (scenario.KeyValues is { } values)
        {
            http.Request.Headers[IdempotencyHeaders.Key] = values;
        }
        else if (scenario.Key is not null)
        {
            http.Request.Headers[IdempotencyHeaders.Key] = scenario.Key;
        }

        var body = scenario.Body ?? [];
        http.Request.Body = new MemoryStream(body);
        http.Request.ContentLength = scenario.Chunked ? null : body.Length;
        http.Request.RouteValues = new RouteValueDictionary { ["id"] = scenario.RouteId };

        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = scenario.NoStore ? new List<object> { new IdempotencyNoStoreAttribute() } : new List<object>(),
        };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var filters = new List<IFilterMetadata>();
        var resource = new ResourceExecutingContext(actionContext, filters, new List<IValueProviderFactory>());
        var ran = false;

        await filter.OnResourceExecutionAsync(resource, async () =>
        {
            ran = true;

            if (scenario.Throws)
            {
                throw new InvalidOperationException("boom");
            }

            var result = scenario.Result ?? new OkObjectResult(new Reply("ok"));
            var executing = new ResultExecutingContext(actionContext, filters, result, new object());
            await filter.OnResultExecutionAsync(
                executing,
                () =>
                {
                    return Task.FromResult(new ResultExecutedContext(actionContext, filters, result, new object()));
                });
            return new ResourceExecutedContext(actionContext, filters);
        });

        return new FilterRun(resource, http, store, ran, logger);
    }

    /// <summary>What one request and action look like to a run.</summary>
    private sealed class Scenario
    {
        /// <summary>The key header value, or null for no header.</summary>
        public string? Key { get; init; } = "key-1";

        /// <summary>Several header values, when the header is repeated; wins over <see cref="Key"/>.</summary>
        public StringValues? KeyValues { get; init; }

        /// <summary>The store, or null for a fresh one that answers <see cref="IdempotencyStarted"/>.</summary>
        public FakeStore? Store { get; init; }

        /// <summary>Whether the container holds no <see cref="IIdempotencyStore"/>, as when the Idempotency module is not composed.</summary>
        public bool StoreNotRegistered { get; init; }

        /// <summary>Whether the container holds no <see cref="IIdempotencyFingerprint"/>.</summary>
        public bool FingerprintNotRegistered { get; init; }

        /// <summary>Who the request runs as; an administrator by default.</summary>
        public Caller? Caller { get; init; }

        /// <summary>The request body.</summary>
        public byte[]? Body { get; init; }

        /// <summary>Whether the body has no declared length.</summary>
        public bool Chunked { get; init; }

        /// <summary>The HTTP method.</summary>
        public string Method { get; init; } = "POST";

        /// <summary>The <c>id</c> route value.</summary>
        public string RouteId { get; init; } = "1";

        /// <summary>What the action returns; a 200 object by default.</summary>
        public IActionResult? Result { get; init; }

        /// <summary>Whether the action carries the no-store marker.</summary>
        public bool NoStore { get; init; }

        /// <summary>Whether the action throws.</summary>
        public bool Throws { get; init; }
    }

    /// <summary>What one filter run produced.</summary>
    /// <param name="Context">The resource context, whose result the filter may have set.</param>
    /// <param name="Http">The request and response.</param>
    /// <param name="Store">The store the filter asked.</param>
    /// <param name="ActionRan">Whether the rest of the pipeline was invoked.</param>
    /// <param name="Logger">What the filter logged.</param>
    private sealed record FilterRun(
        ResourceExecutingContext Context, HttpContext Http, FakeStore Store, bool ActionRan, CapturingLogger Logger);

    /// <summary>A response body for the action to return.</summary>
    /// <param name="Name">A value.</param>
    private sealed record Reply(string Name);

    /// <summary>A current user with a fixed caller.</summary>
    private sealed class FakeUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; }

        /// <summary>Creates the user.</summary>
        /// <param name="caller">Who the request runs as.</param>
        public FakeUser(Caller caller)
        {
            Caller = caller;
        }
    }

    /// <summary>A store that records every call and answers <see cref="Answer"/> to a begin.</summary>
    private sealed class FakeStore : IIdempotencyStore
    {
        /// <summary>The token the default answer reserves with.</summary>
        public static readonly Guid StartedToken = Guid.Parse("66666666-0000-4000-8000-000000000001");

        /// <summary>What <see cref="BeginAsync"/> answers.</summary>
        public IdempotencyBegin Answer { get; init; } = new IdempotencyStarted(StartedToken);

        /// <summary>Whether <see cref="CompleteAsync"/> fails, as when the database is down.</summary>
        public bool CompleteThrows { get; init; }

        /// <summary>The token of every complete call, in order.</summary>
        public List<Guid> CompletedTokens { get; } = new();

        /// <summary>The token of every abandon call, in order.</summary>
        public List<Guid> AbandonedTokens { get; } = new();

        /// <summary>Every begin call, in order.</summary>
        public List<(string CallerKey, string Key, string RequestHash)> Begun { get; } = new();

        /// <summary>Every complete call, in order.</summary>
        public List<(string CallerKey, string Key, int StatusCode, string? ContentType, string? Body)> Completed { get; } = new();

        /// <summary>Every abandon call, in order.</summary>
        public List<(string CallerKey, string Key)> Abandoned { get; } = new();

        /// <inheritdoc />
        public Task<IdempotencyBegin> BeginAsync(string callerKey, string key, string requestHash, CancellationToken ct)
        {
            Begun.Add((callerKey, key, requestHash));
            return Task.FromResult(Answer);
        }

        /// <inheritdoc />
        public Task CompleteAsync(
            string callerKey, string key, Guid token, int statusCode, string? contentType, string? body, CancellationToken ct)
        {
            if (CompleteThrows)
            {
                throw new InvalidOperationException("the database is down (key " + key + ")");
            }

            Completed.Add((callerKey, key, statusCode, contentType, body));
            CompletedTokens.Add(token);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AbandonAsync(string callerKey, string key, Guid token, CancellationToken ct)
        {
            Abandoned.Add((callerKey, key));
            AbandonedTokens.Add(token);
            return Task.CompletedTask;
        }
    }

    /// <summary>A stand-in fingerprint: a plain SHA-256 of the material, which is all these tests need to tell requests apart.</summary>
    private sealed class FakeFingerprint : IIdempotencyFingerprint
    {
        /// <inheritdoc />
        public string Compute(byte[] requestMaterial)
        {
            return Convert.ToHexString(SHA256.HashData(requestMaterial)).ToLowerInvariant();
        }
    }

    /// <summary>A logger that keeps every formatted message and the level it was written at.</summary>
    private sealed class CapturingLogger : ILogger<IdempotencyFilter>
    {
        /// <summary>Every entry written, in order.</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
