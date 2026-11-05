using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Domain.Policies;
using Maran.Modules.Idempotency.Persistence;
using Maran.Modules.Idempotency.Services;
using Maran.Modules.Idempotency.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Idempotency.Tests.Services;

/// <summary>What a request carrying an idempotency key is told, and what the store keeps for its retry.</summary>
public sealed class IdempotencyStoreTests
{
    /// <summary>The caller whose key is used unless a test names another.</summary>
    private const string Caller = IdempotencyTestValues.AdministratorA;

    /// <summary>The clock every store in a test reads.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>The name of this test's private database, shared by every context it opens.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>The first request with a key is told to run.</summary>
    [Fact]
    public async Task The_first_request_with_a_key_is_told_to_run()
    {
        var result = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyStarted>(result);
    }

    /// <summary>A retry of a finished request replays its status, content type and body.</summary>
    [Fact]
    public async Task A_retry_of_a_finished_request_replays_its_stored_response()
    {
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 201, "application/json", "{\"id\":7}", default);

        var retry = await Begin(IdempotencyTestValues.HashOne);

        var replay = Assert.IsType<IdempotencyReplay>(retry);
        Assert.Equal(201, replay.StatusCode);
        Assert.Equal("application/json", replay.ContentType);
        Assert.Equal("{\"id\":7}", replay.Body);
    }

    /// <summary>A response completed with no body replays its status and says the body was not stored.</summary>
    [Fact]
    public async Task A_response_completed_with_no_body_replays_with_a_null_body()
    {
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, null, null, default);

        var retry = await Begin(IdempotencyTestValues.HashOne);

        var replay = Assert.IsType<IdempotencyReplay>(retry);
        Assert.Equal(200, replay.StatusCode);
        Assert.Null(replay.Body);
    }

    /// <summary>A body over the storage cap is dropped, not truncated, and the replay carries no body.</summary>
    [Fact]
    public async Task A_body_over_the_cap_is_not_stored()
    {
        var oversized = new string('x', IdempotencyRecord.ResponseBodyMaxBytes + 1);
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, "text/plain", oversized, default);

        var retry = await Begin(IdempotencyTestValues.HashOne);

        var replay = Assert.IsType<IdempotencyReplay>(retry);
        Assert.Null(replay.Body);
    }

    /// <summary>The same key with a different request is a mismatch, running or finished.</summary>
    [Fact]
    public async Task The_same_key_with_a_different_request_is_a_mismatch()
    {
        var token = await StartAsync();

        var whileRunning = await Begin(IdempotencyTestValues.HashTwo);
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, null, "{}", default);
        var afterFinish = await Begin(IdempotencyTestValues.HashTwo);

        Assert.IsType<IdempotencyMismatch>(whileRunning);
        Assert.IsType<IdempotencyMismatch>(afterFinish);
    }

    /// <summary>The same key and request while the first is still running is in progress, not a second run.</summary>
    [Fact]
    public async Task The_same_request_while_the_first_is_running_is_in_progress()
    {
        await Begin(IdempotencyTestValues.HashOne);

        var second = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyInProgress>(second);
    }

    /// <summary>An abandoned reservation frees the key, so a retry after a server failure runs.</summary>
    [Fact]
    public async Task An_abandoned_reservation_lets_the_retry_run()
    {
        var token = await StartAsync();
        await Store().AbandonAsync(Caller, IdempotencyTestValues.Key, token, default);

        var retry = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyStarted>(retry);
    }

    /// <summary>Abandoning never removes a response that was already stored.</summary>
    [Fact]
    public async Task Abandoning_a_finished_request_keeps_its_stored_response()
    {
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, null, "{}", default);

        await Store().AbandonAsync(Caller, IdempotencyTestValues.Key, token, default);
        var retry = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyReplay>(retry);
    }

    /// <summary>Abandoning or completing a key that was never begun changes nothing and does not throw.</summary>
    [Fact]
    public async Task Completing_or_abandoning_an_unknown_key_is_a_no_op()
    {
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, Guid.NewGuid(), 200, null, "{}", default);
        await Store().AbandonAsync(Caller, IdempotencyTestValues.Key, Guid.NewGuid(), default);

        using var context = InMemoryIdempotencyDatabase.Open(_database);
        Assert.Empty(await context.IdempotencyRecords.ToListAsync());
    }

    /// <summary>The same key used by another caller is independent and never replays another's response.</summary>
    [Fact]
    public async Task The_same_key_for_another_caller_is_independent()
    {
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, null, "{\"secret\":\"a\"}", default);

        var other = await Store().BeginAsync(
            IdempotencyTestValues.AdministratorB, IdempotencyTestValues.Key, IdempotencyTestValues.HashOne, default);
        var first = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyStarted>(other);
        Assert.Equal("{\"secret\":\"a\"}", Assert.IsType<IdempotencyReplay>(first).Body);
    }

    /// <summary>Another caller completing its own request never overwrites the first caller's response.</summary>
    [Fact]
    public async Task Completing_for_one_caller_leaves_the_other_callers_record_alone()
    {
        await Begin(IdempotencyTestValues.HashOne);
        var started = await Store().BeginAsync(
            IdempotencyTestValues.AdministratorB, IdempotencyTestValues.Key, IdempotencyTestValues.HashOne, default);

        await Store().CompleteAsync(
            IdempotencyTestValues.AdministratorB, IdempotencyTestValues.Key, Assert.IsType<IdempotencyStarted>(started).Token,
            200, null, "{\"b\":1}", default);

        var first = await Begin(IdempotencyTestValues.HashOne);
        Assert.IsType<IdempotencyInProgress>(first);
    }

    /// <summary>A reservation whose lease ran out can be taken again.</summary>
    [Fact]
    public async Task An_expired_reservation_can_be_taken_again()
    {
        await Begin(IdempotencyTestValues.HashOne);
        _clock.UtcNow += IdempotencyRetention.ReservationLease + TimeSpan.FromSeconds(1);

        var retry = await Begin(IdempotencyTestValues.HashTwo);

        Assert.IsType<IdempotencyStarted>(retry);
    }

    /// <summary>A stored response stops replaying once the 24-hour window has passed.</summary>
    [Fact]
    public async Task A_stored_response_stops_replaying_after_the_window()
    {
        var token = await StartAsync();
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, token, 200, null, "{}", default);
        _clock.UtcNow += IdempotencyRetention.CompletedWindow + TimeSpan.FromSeconds(1);

        var retry = await Begin(IdempotencyTestValues.HashOne);

        Assert.IsType<IdempotencyStarted>(retry);
    }

    /// <summary>Every reservation is minted its own token, so two reservations never share one.</summary>
    [Fact]
    public async Task Every_reservation_gets_its_own_token()
    {
        var first = await StartAsync();
        var other = Assert.IsType<IdempotencyStarted>(await Store().BeginAsync(
            IdempotencyTestValues.AdministratorB, IdempotencyTestValues.Key, IdempotencyTestValues.HashOne, default));

        Assert.NotEqual(Guid.Empty, first);
        Assert.NotEqual(first, other.Token);
    }

    /// <summary>A slow request whose lease lapsed cannot complete the row of the request that took the key after it.</summary>
    [Fact]
    public async Task A_stale_token_cannot_complete_a_row_another_request_reserved()
    {
        var slow = await StartAsync();
        _clock.UtcNow += IdempotencyRetention.ReservationLease + TimeSpan.FromSeconds(1);
        var second = Assert.IsType<IdempotencyStarted>(await Begin(IdempotencyTestValues.HashOne)).Token;

        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, slow, 201, null, "{\"slow\":1}", default);

        Assert.IsType<IdempotencyInProgress>(await Begin(IdempotencyTestValues.HashOne));
        await Store().CompleteAsync(Caller, IdempotencyTestValues.Key, second, 200, null, "{\"second\":1}", default);
        var replay = Assert.IsType<IdempotencyReplay>(await Begin(IdempotencyTestValues.HashOne));
        Assert.Equal("{\"second\":1}", replay.Body);
    }

    /// <summary>A slow request whose lease lapsed cannot free the row of the request that took the key after it.</summary>
    [Fact]
    public async Task A_stale_token_cannot_abandon_a_row_another_request_reserved()
    {
        var slow = await StartAsync();
        _clock.UtcNow += IdempotencyRetention.ReservationLease + TimeSpan.FromSeconds(1);
        await Begin(IdempotencyTestValues.HashOne);

        await Store().AbandonAsync(Caller, IdempotencyTestValues.Key, slow, default);

        Assert.IsType<IdempotencyInProgress>(await Begin(IdempotencyTestValues.HashOne));
    }

    /// <summary>A key or caller key the policy refuses never reaches the database.</summary>
    [Theory]
    [InlineData("", IdempotencyTestValues.Key, IdempotencyTestValues.HashOne)]
    [InlineData(Caller, "", IdempotencyTestValues.HashOne)]
    [InlineData(Caller, "has space", IdempotencyTestValues.HashOne)]
    [InlineData(Caller, "line\nbreak", IdempotencyTestValues.HashOne)]
    [InlineData(Caller, IdempotencyTestValues.Key, "not-a-hash")]
    public async Task A_value_the_policy_refuses_is_rejected_before_any_query(string callerKey, string key, string hash)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().BeginAsync(callerKey, key, hash, default));

        using var context = InMemoryIdempotencyDatabase.Open(_database);
        Assert.Empty(await context.IdempotencyRecords.ToListAsync());
    }

    /// <summary>A key longer than 255 characters is refused.</summary>
    [Fact]
    public async Task A_key_over_the_length_limit_is_rejected()
    {
        var tooLong = new string('k', IdempotencyKeyPolicy.KeyMaxLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Store().BeginAsync(Caller, tooLong, IdempotencyTestValues.HashOne, default));
    }

    /// <summary>Begins the shared key for the shared caller with the given request hash.</summary>
    /// <param name="requestHash">The hash of the request.</param>
    /// <returns>What the store tells the request.</returns>
    private Task<IdempotencyBegin> Begin(string requestHash)
    {
        return Store().BeginAsync(Caller, IdempotencyTestValues.Key, requestHash, default);
    }

    /// <summary>Begins the shared key and returns the token the reservation was minted with.</summary>
    /// <returns>The reservation token.</returns>
    private async Task<Guid> StartAsync()
    {
        return Assert.IsType<IdempotencyStarted>(await Begin(IdempotencyTestValues.HashOne)).Token;
    }

    /// <summary>A store over a fresh context of this test's database, as another request's scope would hold.</summary>
    /// <returns>The store.</returns>
    private IdempotencyStore Store()
    {
        return new IdempotencyStore(InMemoryIdempotencyDatabase.Open(_database), _clock);
    }
}
