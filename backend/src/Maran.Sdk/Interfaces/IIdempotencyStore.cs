using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The durable record of idempotency keys, implemented by the Idempotency module and consumed by
/// <see cref="Filters.IdempotencyFilter"/>. A key is scoped to its caller: the same string sent by two
/// callers is two different keys, so one caller can neither read nor block another's.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Looks the key up and, when it is new, reserves it in one atomic step, so two concurrent requests
    /// with one key cannot both be told to run.
    /// </summary>
    /// <param name="callerKey">Who the key belongs to (<c>user:&lt;id&gt;</c> or <c>key:&lt;id&gt;</c>).</param>
    /// <param name="key">The caller's <c>Idempotency-Key</c>.</param>
    /// <param name="requestHash">The keyed fingerprint of the request (<see cref="IIdempotencyFingerprint"/>); a stored key with another fingerprint is a mismatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One of the four <see cref="IdempotencyBegin"/> cases.</returns>
    Task<IdempotencyBegin> BeginAsync(string callerKey, string key, string requestHash, CancellationToken ct);

    /// <summary>
    /// Records the answer of the request that reserved the key, so later identical requests replay it.
    /// Acts only on the still-running reservation that <paramref name="token"/> names; a stale token
    /// (the lease lapsed and another request reserved the key) changes nothing.
    /// </summary>
    /// <param name="callerKey">Who the key belongs to.</param>
    /// <param name="key">The caller's <c>Idempotency-Key</c>.</param>
    /// <param name="token">The token <see cref="IdempotencyStarted"/> carried when the reservation was made.</param>
    /// <param name="statusCode">The HTTP status the request answered with.</param>
    /// <param name="contentType">The answer's content type, or null when no body is stored.</param>
    /// <param name="body">The serialized answer, or null when it had none or must not be stored.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteAsync(
        string callerKey, string key, Guid token, int statusCode, string? contentType, string? body, CancellationToken ct);

    /// <summary>
    /// Releases a reservation whose request produced nothing worth keeping, so the caller may retry the
    /// key. Acts only on the still-running reservation that <paramref name="token"/> names.
    /// </summary>
    /// <param name="callerKey">Who the key belongs to.</param>
    /// <param name="key">The caller's <c>Idempotency-Key</c>.</param>
    /// <param name="token">The token <see cref="IdempotencyStarted"/> carried when the reservation was made.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AbandonAsync(string callerKey, string key, Guid token, CancellationToken ct);
}
