using System.Text;
using Maran.Modules.Idempotency.Domain.Enums;
using Maran.Modules.Idempotency.Domain.Policies;

namespace Maran.Modules.Idempotency.Domain.Entities;

/// <summary>
/// One request that carried an idempotency key: the reservation made when it started and, once it
/// finished, the response a retry is answered with (issue #136).
/// </summary>
/// <remarks>
/// <para>
/// <b>The identity of a record is the pair <see cref="CallerKey"/> and <see cref="Key"/>, never the key
/// alone.</b> Two callers who happen to choose the same key hold two unrelated records, and the lookup
/// is always by both, so a response is replayable only by the principal that made the request.
/// </para>
/// <para>
/// <b>A stored response is customer data at rest in clear</b> and is therefore kept for
/// <see cref="IdempotencyRetention.CompletedWindow"/> at most, then purged. A response over
/// <see cref="ResponseBodyMaxBytes"/> is not stored: the record completes with no body and a replay
/// says so, rather than truncating it into something that looks complete.
/// </para>
/// </remarks>
public sealed class IdempotencyRecord
{
    /// <summary>The most bytes of response body a record keeps: 256 KiB.</summary>
    public const int ResponseBodyMaxBytes = 256 * 1024;

    /// <summary>The longest a stored content type may be.</summary>
    public const int ContentTypeMaxLength = 255;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The principal that made the request, as a typed string; one half of the identity.</summary>
    public string CallerKey { get; private set; }

    /// <summary>The key from the client's <c>Idempotency-Key</c> header; the other half of the identity.</summary>
    public string Key { get; private set; }

    /// <summary>
    /// The keyed fingerprint of the request (HMAC-SHA256 as lowercase hexadecimal, never a plain digest:
    /// a body can carry a password); a reused key with another fingerprint is refused.
    /// </summary>
    public string RequestHash { get; private set; }

    /// <summary>
    /// Names the request that made the reservation; null once the record is completed. Only the request
    /// holding this value may complete or release the running reservation, so a slow request whose lease
    /// lapsed cannot touch the row of the request that took the key after it.
    /// </summary>
    public Guid? ReservationToken { get; private set; }

    /// <summary>Whether the request is still running or has finished.</summary>
    public IdempotencyState State { get; private set; }

    /// <summary>The response status to replay; null until <see cref="Complete"/> is called.</summary>
    public int? StatusCode { get; private set; }

    /// <summary>The response body to replay; null while running, and when it was not stored.</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>The response content type to replay, when the response had one.</summary>
    public string? ContentType { get; private set; }

    /// <summary>When the key was first used.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the record stops counting: the end of the reservation lease while running, the end of the
    /// completed window once finished. The purge removes a record past this instant.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Reserves a key for a request that is starting.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="callerKey">The principal making the request.</param>
    /// <param name="key">The client's key.</param>
    /// <param name="requestHash">The keyed fingerprint of the request as lowercase hexadecimal.</param>
    /// <param name="reservationToken">The token the reserving request will present to complete or release the row.</param>
    /// <param name="now">When the request started.</param>
    /// <exception cref="ArgumentException">A value is not one the policy accepts; the caller is buggy.</exception>
    public IdempotencyRecord(
        Guid id, string callerKey, string key, string requestHash, Guid reservationToken, DateTimeOffset now)
    {
        if (!IdempotencyKeyPolicy.IsValidCallerKey(callerKey))
        {
            throw new ArgumentException("The caller key is not acceptable.", nameof(callerKey));
        }

        if (!IdempotencyKeyPolicy.IsValidKey(key))
        {
            throw new ArgumentException("The key is not acceptable.", nameof(key));
        }

        if (!IdempotencyKeyPolicy.IsValidRequestHash(requestHash))
        {
            throw new ArgumentException("The request hash is not acceptable.", nameof(requestHash));
        }

        Id = id;
        CallerKey = callerKey;
        Key = key;
        RequestHash = requestHash;
        ReservationToken = reservationToken;
        State = IdempotencyState.InProgress;
        CreatedAt = now;
        ExpiresAt = now + IdempotencyRetention.ReservationLease;
    }

    /// <summary>EF Core's constructor.</summary>
    private IdempotencyRecord()
    {
        CallerKey = string.Empty;
        Key = string.Empty;
        RequestHash = string.Empty;
    }

    /// <summary>True when the record is a running reservation that <paramref name="token"/> names.</summary>
    /// <param name="token">The token a request presents.</param>
    /// <returns>False for a completed record, and for a running one made by another request.</returns>
    public bool IsReservedBy(Guid token)
    {
        return State == IdempotencyState.InProgress && ReservationToken == token;
    }

    /// <summary>True when the record has stopped counting at <paramref name="now"/>.</summary>
    /// <param name="now">The instant to test against.</param>
    /// <returns>True when <see cref="ExpiresAt"/> is not after <paramref name="now"/>.</returns>
    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt <= now;
    }

    /// <summary>
    /// Records the response of the finished request and extends the record to the completed window,
    /// counted from <see cref="CreatedAt"/>. A record that is already completed keeps its first
    /// response: a second finish does not rewrite what a retry may already have replayed.
    /// </summary>
    /// <param name="statusCode">The response status.</param>
    /// <param name="contentType">The response content type, or null.</param>
    /// <param name="body">
    /// The response body, or null when it must not be stored (the action returns a secret) or is not
    /// there. A body over <see cref="ResponseBodyMaxBytes"/> is dropped, never truncated.
    /// </param>
    public void Complete(int statusCode, string? contentType, string? body)
    {
        if (State == IdempotencyState.Completed)
        {
            return;
        }

        State = IdempotencyState.Completed;
        ReservationToken = null;
        StatusCode = statusCode;
        ContentType = contentType is not null && contentType.Length > ContentTypeMaxLength ? null : contentType;
        ResponseBody = body is not null && Encoding.UTF8.GetByteCount(body) <= ResponseBodyMaxBytes ? body : null;
        ExpiresAt = CreatedAt + IdempotencyRetention.CompletedWindow;
    }
}
