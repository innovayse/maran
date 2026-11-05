using System.Text;
using System.Text.Json;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Sdk.Filters;

/// <summary>
/// Makes an action <see cref="IdempotentAttribute">idempotent</see> for callers that send an
/// <c>Idempotency-Key</c> header: the first request runs and its answer is stored, an identical repeat
/// is answered from the store, and a key reused for a different request is refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two filter stages, because one cannot do it.</b> The request must be fingerprinted before model
/// binding consumes the body, which only a resource filter sees; the answer must be inspected before
/// the result writes it, which only a result filter sees. The two halves share one instance and hand
/// the reservation to each other through <see cref="HttpContext.Items"/>.
/// </para>
/// <para>
/// <b>Answers.</b> No header: the action runs as before. A header that is empty, repeated, longer than
/// <see cref="IdempotencyLimits.MaxKeyLength"/> or outside <c>[A-Za-z0-9_:.-]</c>, or a body larger than
/// <see cref="IdempotencyLimits.MaxRequestBodyBytes"/>: 400 <c>IdempotencyKeyInvalid</c> (a body that
/// cannot be fingerprinted is refused rather than run unprotected, because the caller asked for a
/// guarantee the filter cannot give). A key used by a different request: 422 <c>IdempotencyKeyReused</c>.
/// A key whose first request is still running: 409 <c>IdempotencyRequestInProgress</c>. A finished
/// identical request: the stored status, body and content type with <c>Idempotent-Replayed: true</c>.
/// An anonymous caller has no identity to scope a key to, so its requests pass through unchanged.
/// </para>
/// <para>
/// <b>No store, no idempotency, no failure.</b> The store is implemented by the Idempotency module and
/// no manifest dependency forces that module to be composed (nothing reads <c>Manifest.Dependencies</c>),
/// so a panel that composes a module tagged <see cref="IdempotentAttribute"/> without it, or a future
/// build in which the module is locked or absent, must not answer every tagged action with a 500. When
/// the container has no <see cref="IIdempotencyStore"/>, the filter passes the request through exactly
/// as if the header had not been sent. The store is therefore asked for with
/// <see cref="IServiceProvider.GetService"/> at request time, the ONE sanctioned service location in the
/// Sdk: a constructor parameter would make the missing module a construction failure instead of a
/// degraded guarantee, and an optional constructor parameter would hide the same decision in the
/// activator. The degradation is silent to the caller by design of the header (no header, no promise);
/// the response then lacks the stored-answer behaviour, which is stated here rather than hidden.
/// </para>
/// <para>
/// <b>No fingerprint service, no idempotency, no failure.</b> The request is fingerprinted with the
/// Host's <see cref="IIdempotencyFingerprint"/>, keyed on the panel's secret so a password in a body is
/// not guessable from the stored value. It is looked up at request time like the store and, when none
/// is registered, the request passes through exactly as if no header had been sent.
/// </para>
/// <para>
/// <b>What is stored.</b> Only a 2xx answer, or a 4xx other than 401, 403, 408 and 429 (answers about the
/// caller's credentials or the moment), of an object or status-code result whose serialized
/// body is at most <see cref="IdempotencyLimits.MaxStoredBodyBytes"/>: those are deterministic for an
/// identical request. A 5xx, an exception, a result of another kind or an oversized body releases the
/// reservation so the caller can retry the key. A replay does not repeat headers the action set
/// (such as <c>Location</c>). An action marked <see cref="IdempotencyNoStoreAttribute"/> keeps the key
/// and the fingerprint but never the body, and a replay of it is 409 <c>IdempotencyResponseNotStored</c>.
/// </para>
/// <para>
/// <b>A reservation belongs to the request that made it.</b> The store mints a token with the
/// reservation and the filter passes it back to complete or release it, so a request that outlives the
/// reservation lease cannot complete or release the row of the request that later took the key.
/// </para>
/// <para>
/// <b>A failure to store is not the request's failure.</b> When the action has already succeeded and
/// the store cannot record its answer (the database is down), the response is still the action's own:
/// the failure is logged once, without the key, and the reservation lapses with its lease. Turning a
/// completed action into a 500 would make the caller retry work that has already been done.
/// </para>
/// <para>
/// <b>Stated limit.</b> The answer is stored just before it is written, not after: a connection that
/// breaks during the write still leaves the key completed, which is what a retrying caller needs.
/// </para>
/// </remarks>
public sealed class IdempotencyFilter : IAsyncResourceFilter, IAsyncResultFilter
{
    /// <summary>Where the resource stage leaves the reservation for the result stage.</summary>
    private const string ReservationItemKey = "Maran.Idempotency.Reservation";

    /// <summary>The authenticated principal of the current request.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The JSON options MVC serializes responses with, so a stored body is the body the caller would have received.</summary>
    private readonly JsonSerializerOptions _json;

    /// <summary>Where a failure to store an answer is reported.</summary>
    private readonly ILogger<IdempotencyFilter> _logger;

    /// <summary>Creates the filter from the container.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="jsonOptions">The JSON options MVC serializes responses with.</param>
    /// <param name="logger">Where a failure to store an answer is reported.</param>
    public IdempotencyFilter(
        ICurrentUser currentUser,
        IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions,
        ILogger<IdempotencyFilter> logger)
    {
        _currentUser = currentUser;
        _json = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
    }

    /// <summary>Reserves, replays or refuses before the action binds its body; releases an unfinished reservation afterwards.</summary>
    /// <param name="context">The request about to be routed to its action.</param>
    /// <param name="next">Runs the rest of the pipeline.</param>
    /// <returns>A task that completes when the pipeline has run or a refusal has been set.</returns>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(IdempotencyHeaders.Key, out var header))
        {
            await next();
            return;
        }

        var callerKey = IdempotencyCallerKey.Of(_currentUser.Caller);

        if (callerKey is null)
        {
            await next();
            return;
        }

        // The one sanctioned service location (see the class remarks): no store means no idempotency, not a 500.
        var store = http.RequestServices.GetService<IIdempotencyStore>();

        if (store is null)
        {
            await next();
            return;
        }

        // The same degrade rule for the keyed fingerprint: without it nothing can be compared safely.
        var fingerprint = http.RequestServices.GetService<IIdempotencyFingerprint>();

        if (fingerprint is null)
        {
            await next();
            return;
        }

        if (header.Count != 1 || !IsValidKey(header[0]))
        {
            context.Result = IdempotencyRefusal.Of(http, IdempotencyErrorCodes.KeyInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var key = header[0]!;
        var hash = await IdempotencyRequestHash.ComputeAsync(http.Request, fingerprint, http.RequestAborted);

        if (hash is null)
        {
            context.Result = IdempotencyRefusal.Of(http, IdempotencyErrorCodes.KeyInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var begin = await store.BeginAsync(callerKey, key, hash, http.RequestAborted);

        switch (begin)
        {
            case IdempotencyStarted started:
                http.Items[ReservationItemKey] = new Reservation(store, callerKey, key, started.Token);
                break;
            case IdempotencyReplay replay:
                context.Result = ReplayOf(context, replay);
                return;
            case IdempotencyMismatch:
                context.Result = IdempotencyRefusal.Of(http, IdempotencyErrorCodes.KeyReused, StatusCodes.Status422UnprocessableEntity);
                return;
            case IdempotencyInProgress:
                context.Result = IdempotencyRefusal.Of(http, IdempotencyErrorCodes.RequestInProgress, StatusCodes.Status409Conflict);
                return;
            default:
                throw new InvalidOperationException("Unknown idempotency answer: " + begin.GetType().Name);
        }

        try
        {
            await next();
        }
        finally
        {
            // Still reserved here means the result stage never stored an answer: an exception, a 5xx, a
            // result of a kind that is not stored, or a short-circuit. Release it so the key can be retried.
            // None is cancellable: the reservation must be released even when the caller has gone away.
            if (http.Items.TryGetValue(ReservationItemKey, out var left) && left is Reservation reservation)
            {
                http.Items.Remove(ReservationItemKey);
                await reservation.Store.AbandonAsync(reservation.CallerKey, reservation.Key, reservation.Token, CancellationToken.None);
            }
        }
    }

    /// <summary>Stores the answer the action produced, when it is the kind of answer that is stored.</summary>
    /// <param name="context">The result about to be executed.</param>
    /// <param name="next">Executes the result.</param>
    /// <returns>A task that completes when the result has been executed.</returns>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;

        if (http.Items.TryGetValue(ReservationItemKey, out var held) && held is Reservation reservation)
        {
            await TryStoreAsync(context, reservation);
        }

        await next();
    }

    /// <summary>Completes the reservation when the result is storable; otherwise leaves it for the resource stage to release.</summary>
    /// <param name="context">The result about to be executed.</param>
    /// <param name="reservation">The reservation the resource stage made.</param>
    private async Task TryStoreAsync(ResultExecutingContext context, Reservation reservation)
    {
        var http = context.HttpContext;
        var noStore = HasNoStoreMarker(context.ActionDescriptor);
        int status;
        string? contentType = null;
        string? body = null;

        switch (context.Result)
        {
            case ObjectResult objectResult:
                status = objectResult.StatusCode ?? (objectResult.Value as ProblemDetails)?.Status ?? StatusCodes.Status200OK;

                if (objectResult.Value is not null && !noStore)
                {
                    body = JsonSerializer.Serialize(
                        objectResult.Value, objectResult.DeclaredType ?? objectResult.Value.GetType(), _json);
                    contentType = objectResult.Value is ProblemDetails ? "application/problem+json" : "application/json";
                }

                break;
            case StatusCodeResult statusResult:
                status = statusResult.StatusCode;
                break;
            default:
                return;
        }

        // 401, 403, 408 and 429 are answers about the caller's credentials or the moment, not about the
        // request: replaying one would pin a refusal that a later retry (new permission, calmer minute)
        // would not get, so they release the key like a 5xx.
        var storable = status is >= 200 and < 300
            or (>= 400 and < 500 and not 401 and not 403 and not 408 and not 429);

        if (!storable || (body is not null && Encoding.UTF8.GetByteCount(body) > IdempotencyLimits.MaxStoredBodyBytes))
        {
            return;
        }

        http.Items.Remove(ReservationItemKey);

        try
        {
            await reservation.Store.CompleteAsync(
                reservation.CallerKey, reservation.Key, reservation.Token, status, contentType, body, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The action has already succeeded; the answer it produced is the one the caller gets. Only the
            // exception's type is logged: a database message can quote the key and the caller key.
            _logger.LogWarning(
                "The idempotency store could not record the answer of a request ({ExceptionType}); the answer is sent as it is "
                + "and the key stays reserved until its lease lapses.",
                exception.GetType().Name);
        }
    }

    /// <summary>Builds the response for a stored answer, or the refusal when the action never stores its answer.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="replay">The stored answer.</param>
    /// <returns>The result to short-circuit the pipeline with.</returns>
    private static IActionResult ReplayOf(ResourceExecutingContext context, IdempotencyReplay replay)
    {
        var http = context.HttpContext;

        if (HasNoStoreMarker(context.ActionDescriptor))
        {
            return IdempotencyRefusal.Of(http, IdempotencyErrorCodes.ResponseNotStored, StatusCodes.Status409Conflict);
        }

        http.Response.Headers[IdempotencyHeaders.Replayed] = "true";

        if (replay.Body is null)
        {
            return new StatusCodeResult(replay.StatusCode);
        }

        return new ContentResult
        {
            StatusCode = replay.StatusCode,
            Content = replay.Body,
            ContentType = replay.ContentType ?? "application/json",
        };
    }

    /// <summary>Whether the action carries <see cref="IdempotencyNoStoreAttribute"/>.</summary>
    /// <param name="action">The action the request was routed to.</param>
    /// <returns>True when its answer must never be stored.</returns>
    private static bool HasNoStoreMarker(ActionDescriptor action)
    {
        return action.EndpointMetadata is not null && action.EndpointMetadata.OfType<IdempotencyNoStoreAttribute>().Any();
    }

    /// <summary>Whether a header value is a well-formed key: 1 to 255 characters of <c>[A-Za-z0-9_:.-]</c>.</summary>
    /// <param name="value">The header value.</param>
    /// <returns>True when acceptable.</returns>
    private static bool IsValidKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > IdempotencyLimits.MaxKeyLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                or '_' or ':' or '.' or '-';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What the resource stage reserved: in which store, whose key, and which.</summary>
    /// <param name="Store">The store the reservation was made in, so the later stages use the one that was found.</param>
    /// <param name="CallerKey">Who the key belongs to.</param>
    /// <param name="Key">The caller's <c>Idempotency-Key</c>.</param>
    /// <param name="Token">The token the store minted with the reservation, passed back to complete or release it.</param>
    private sealed record Reservation(IIdempotencyStore Store, string CallerKey, string Key, Guid Token);
}
