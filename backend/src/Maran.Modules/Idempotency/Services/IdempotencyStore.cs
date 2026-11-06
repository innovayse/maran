using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Domain.Enums;
using Maran.Modules.Idempotency.Domain.Policies;
using Maran.Modules.Idempotency.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Idempotency.Services;

/// <summary>
/// The PostgreSQL-backed <see cref="IIdempotencyStore"/>: decides what a request carrying an idempotency
/// key is, and keeps the response a retry is answered with (issue #136).
/// </summary>
/// <remarks>
/// <para>
/// <b>A reservation is one insert, and the unique index on caller and key is the arbiter.</b> Of two
/// requests that begin the same key at the same instant, exactly one insert succeeds and that request
/// runs; the other sees the violation, reads the winner's row and answers "in progress" (or replays,
/// if the winner has already finished). Nothing here locks a row or holds a transaction open across the
/// action, so a slow request never blocks another caller's database work.
/// </para>
/// <para>
/// <b>A reused key with another request is refused before its state is looked at.</b> The hash is
/// compared first, so a different request never learns that the key is mid-flight and never receives a
/// replay of somebody's response; it is told the key was reused.
/// </para>
/// <para>
/// <b>Expiry is read, not only purged.</b> The hourly purge removes aged rows, but a row past its
/// expiry is treated as absent the moment <c>Begin</c> sees it, so the 5-minute reservation lease and
/// the 24-hour replay window hold to the minute and not to the purge's cadence.
/// </para>
/// <para>
/// <b>A reservation is completed or released only by the request that made it.</b> Each reservation
/// stores a token that <see cref="IdempotencyStarted"/> hands to its request; <c>Complete</c> and
/// <c>Abandon</c> act on the running row only when the token matches. A slow request that outlives its
/// lease, whose key another request has since reserved, therefore finds a different token and changes
/// nothing: it can neither store its answer over the other request's row nor free that row. It cannot
/// stop the work running twice (see <see cref="IdempotencyRetention.ReservationLease"/>); it stops the
/// two requests corrupting one record.
/// </para>
/// <para>
/// <b>Stated limit:</b> storing the response is a separate write from the action's own commit, so a
/// crash between them runs the action and records nothing; the retry then runs it again. This narrows
/// duplicate execution; it does not make it exactly-once.
/// </para>
/// </remarks>
public sealed class IdempotencyStore : IIdempotencyStore
{
    /// <summary>How many times <see cref="BeginAsync"/> re-reads after losing a race before it gives up.</summary>
    private const int MaxAttempts = 3;

    /// <summary>The module's database context.</summary>
    private readonly IdempotencyDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the store.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The injected time source expiry is measured against.</param>
    public IdempotencyStore(IdempotencyDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">A value is not one <see cref="IdempotencyKeyPolicy"/> accepts.</exception>
    /// <exception cref="InvalidOperationException">The key kept changing under concurrent requests.</exception>
    public async Task<IdempotencyBegin> BeginAsync(
        string callerKey, string key, string requestHash, CancellationToken ct)
    {
        RequireValid(callerKey, key);
        if (!IdempotencyKeyPolicy.IsValidRequestHash(requestHash))
        {
            throw new ArgumentException("The request hash is not acceptable.", nameof(requestHash));
        }

        var now = _clock.UtcNow;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var existing = await FindAsync(callerKey, key, ct);

            if (existing is null)
            {
                var token = Guid.NewGuid();
                var reservation = new IdempotencyRecord(Guid.NewGuid(), callerKey, key, requestHash, token, now);
                if (await TrySaveAsync(() =>
                {
                    _dbContext.IdempotencyRecords.Add(reservation);
                }, ct))
                {
                    return new IdempotencyStarted(token);
                }

                continue;
            }

            if (existing.IsExpired(now))
            {
                await TrySaveAsync(() =>
                {
                    _dbContext.IdempotencyRecords.Remove(existing);
                }, ct);
                continue;
            }

            return Judge(existing, requestHash);
        }

        throw new InvalidOperationException("The idempotency key kept changing under concurrent requests.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// A cancelled token (the client went away) stores nothing; the reservation then lapses with its
    /// lease and the key can be used again. A record that is gone, already completed, or reserved by
    /// another request (the token does not match) is left alone.
    /// </remarks>
    public async Task CompleteAsync(
        string callerKey, string key, Guid token, int statusCode, string? contentType, string? body, CancellationToken ct)
    {
        RequireValid(callerKey, key);

        var record = await FindAsync(callerKey, key, ct);
        if (record is null || !record.IsReservedBy(token))
        {
            return;
        }

        await TrySaveAsync(() =>
        {
            record.Complete(statusCode, contentType, body);
        }, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the reservation that <paramref name="token"/> names, and only while it is still running, is
    /// removed. A completed record keeps its stored response: abandoning is for a request that failed on
    /// the server and left nothing to replay; a row another request reserved is not this request's to free.
    /// </remarks>
    public async Task AbandonAsync(string callerKey, string key, Guid token, CancellationToken ct)
    {
        RequireValid(callerKey, key);

        var record = await FindAsync(callerKey, key, ct);
        if (record is null || !record.IsReservedBy(token))
        {
            return;
        }

        await TrySaveAsync(() =>
        {
            _dbContext.IdempotencyRecords.Remove(record);
        }, ct);
    }

    /// <summary>Refuses a caller key or key the policy does not accept, before any query runs.</summary>
    /// <param name="callerKey">The principal making the request.</param>
    /// <param name="key">The client's key.</param>
    /// <exception cref="ArgumentException">A value is not acceptable; the caller is buggy.</exception>
    private static void RequireValid(string callerKey, string key)
    {
        if (!IdempotencyKeyPolicy.IsValidCallerKey(callerKey))
        {
            throw new ArgumentException("The caller key is not acceptable.", nameof(callerKey));
        }

        if (!IdempotencyKeyPolicy.IsValidKey(key))
        {
            throw new ArgumentException("The key is not acceptable.", nameof(key));
        }
    }

    /// <summary>What a retry of an existing, unexpired record is told.</summary>
    /// <param name="existing">The record found for this caller and key.</param>
    /// <param name="requestHash">The hash of the request being made now.</param>
    /// <returns>Mismatch for another request, in progress while running, otherwise the stored response.</returns>
    private static IdempotencyBegin Judge(IdempotencyRecord existing, string requestHash)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyMismatch();
        }

        if (existing.State == IdempotencyState.InProgress)
        {
            return new IdempotencyInProgress();
        }

        return new IdempotencyReplay(existing.StatusCode ?? 0, existing.ContentType, existing.ResponseBody);
    }

    /// <summary>Reads the record of one caller and key, always by both.</summary>
    /// <param name="callerKey">The principal making the request.</param>
    /// <param name="key">The client's key.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The tracked record, or null when there is none.</returns>
    private Task<IdempotencyRecord?> FindAsync(string callerKey, string key, CancellationToken ct)
    {
        return _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(record => record.CallerKey == callerKey && record.Key == key, ct);
    }

    /// <summary>
    /// Applies one change and saves it, reporting a lost race as false instead of an exception. The
    /// tracker is cleared on a failure so the next read starts from what the database holds.
    /// </summary>
    /// <param name="change">Stages the change on the tracked context.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>True when the change was saved; false when the database refused it as a conflict.</returns>
    /// <remarks>
    /// Any <see cref="DbUpdateException"/> is treated as the race, deliberately without reading the
    /// provider's error code: if the failure was something else (the database is down), the re-read that
    /// follows raises that failure itself, so nothing is masked and this module needs no provider type.
    /// </remarks>
    private async Task<bool> TrySaveAsync(Action change, CancellationToken ct)
    {
        change();

        try
        {
            await _dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
