using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Sends the two ACME requests that are safe to repeat — the directory GET and the nonce HEAD — with
/// a small bounded retry, and nothing else.
/// </summary>
/// <remarks>
/// The retry lives here rather than in the Host's <c>AcmePipeline</c> because the pipeline cannot
/// tell these two apart from the signed POSTs that make up the rest of a conversation. A signed POST
/// carries a one-shot nonce inside its signature: replaying the identical body is refused with
/// <c>badNonce</c> every time, and a replayed <c>newOrder</c> after a read timeout can create a
/// SECOND order at the authority and spend the account's order budget. So the pipeline holds the
/// timeout only, <c>AcmeSession</c> re-signs once on <c>badNonce</c>, and these two unsigned,
/// idempotent, side-effect-free requests are the only ones anything replays.
///
/// A request MESSAGE cannot be sent twice — <see cref="HttpClient"/> disposes its content — so the
/// caller hands over a factory and each attempt builds its own.
/// </remarks>
public static class AcmeTransport
{
    /// <summary>Attempts after the first, before the call is given up on.</summary>
    private const int MaxRetryAttempts = 1;

    /// <summary>How long to wait before the single retry.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Pre-compiled log delegate for a retried idempotent request.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRetry =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(AcmeTransport)),
            "Retrying the idempotent ACME request {Url} once after a transport failure");

    /// <summary>Sends one idempotent request, retrying once on a transport failure or a 5xx.</summary>
    /// <param name="http">The named, already-timeout-governed ACME client.</param>
    /// <param name="url">The target URL, for the log line.</param>
    /// <param name="build">Builds a fresh request message for each attempt.</param>
    /// <param name="logger">Where a retry is noted.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The response, or <c>null</c> when both attempts failed at the transport.</returns>
    /// <remarks>
    /// A 4xx is returned rather than retried: it is the authority's decision, and asking again spends
    /// rate-limit budget on the same refusal. Nothing here inspects 429 specially because nothing
    /// here retries any 4xx at all — a special case for 429 inside a 5xx-only predicate is the dead
    /// code this replaced.
    /// </remarks>
    public static async Task<HttpResponseMessage?> SendIdempotentAsync(
        HttpClient http,
        string url,
        Func<HttpRequestMessage> build,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                LogRetry(logger, url, null);
                await Task.Delay(RetryDelay, cancellationToken);
            }

            response?.Dispose();
            response = await TrySendAsync(http, build, cancellationToken);

            if (response is not null && (int)response.StatusCode < 500)
            {
                return response;
            }
        }

        return response;
    }

    /// <summary>Sends one attempt, turning a transport failure into a null rather than an exception.</summary>
    /// <param name="http">The client to send with.</param>
    /// <param name="build">Builds the request message for this attempt.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The response, or <c>null</c> when the attempt could not reach the authority.</returns>
    /// <remarks>
    /// <see cref="OperationCanceledException"/> is deliberately NOT swallowed when the caller asked
    /// for cancellation: a shutdown must stop the pass rather than be reported as an unreachable
    /// authority. A timeout the pipeline imposed surfaces as the same exception type with the
    /// caller's token un-cancelled, and that one is a transport failure.
    /// </remarks>
    private static async Task<HttpResponseMessage?> TrySendAsync(
        HttpClient http,
        Func<HttpRequestMessage> build,
        CancellationToken cancellationToken)
    {
        using var request = build();

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
