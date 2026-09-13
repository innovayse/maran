using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline every call to a certificate authority goes through (rules/csharp.md
/// "Every outbound call goes through a named resilience pipeline"). Attached to the named
/// <c>acme</c> <see cref="HttpClient"/> as a handler, so the Ssl module obtains a client that is
/// already governed and cannot accidentally make an ungoverned call.
/// </summary>
/// <remarks>
/// It is a TIMEOUT and nothing else, and the absence of a retry here is the design rather than an
/// omission.
///
/// Every ACME request but the directory fetch and the nonce HEAD is a signed JWS whose protected
/// header carries a one-shot anti-replay nonce. A retry at this layer replays the identical body:
/// the nonce has already been consumed, so the replay is refused with <c>badNonce</c> every single
/// time, and the session that owns the nonce cannot see the refusal because it happened inside the
/// handler. Worse, a read timeout or a 5xx on <c>newOrder</c> may well follow an order the authority
/// already created, so the replay creates a SECOND order and spends the "new orders per account"
/// budget — the exact rate-limit damage a retry policy here would be written to prevent.
///
/// So retrying belongs where the request can be re-signed, and that is what happens: <c>AcmeSession</c>
/// re-signs once on <c>badNonce</c>, and <c>AcmeClient</c> retries the two unsigned, idempotent calls
/// (the directory GET and the nonce HEAD) itself. A previous version of this file retried 5xx here
/// and excluded 429 in a clause that could never fire — 429 is below 500 — which is how a policy
/// that did the wrong thing came to look careful.
///
/// The timeout is still worth having here rather than on the client: it bounds one attempt, and the
/// module's own retry then gets a fresh budget per attempt instead of racing a single outer deadline.
/// </remarks>
public static class AcmePipeline
{
    /// <summary>The name this pipeline and its <see cref="HttpClient"/> are registered under.</summary>
    public const string Name = "acme";

    /// <summary>Configures the timeout strategy on <paramref name="builder"/>.</summary>
    /// <param name="builder">The HTTP pipeline being built for the named client.</param>
    /// <param name="timeout">
    /// How long one call to the authority may take, from <c>Acme:RequestTimeoutSeconds</c>. This is
    /// the only deadline in play: <c>SslModule</c> leaves <see cref="HttpClient.Timeout"/> infinite
    /// so that the value an operator configures is the value that applies, rather than being cut
    /// short by a second, identical outer budget.
    /// </param>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TimeSpan timeout)
    {
        builder.AddTimeout(timeout);
    }
}
