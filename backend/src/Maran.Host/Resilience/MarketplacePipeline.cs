using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline every call to the marketplace host goes through (rules/csharp.md "Every
/// outbound call goes through a named resilience pipeline"). Attached to the named <c>marketplace</c>
/// <see cref="HttpClient"/> as a handler, so the module obtains a client that is already governed and
/// cannot accidentally make an ungoverned call.
/// </summary>
/// <remarks>
/// <para>
/// It is a TIMEOUT and nothing else, and the retry lives in the module's transport instead, the same split
/// the ACME pipeline makes. The marketplace has exactly one request that must never be repeated: the
/// download-grant POST, whose grant is single use, so a replay after a read timeout would spend a grant
/// the first attempt may already have obtained. A pipeline handler sees an exception outcome with no
/// request attached, so it cannot tell that POST from a catalogue GET; the transport can, and
/// <c>MarketplaceHttpTransport</c> retries a GET up to twice and a POST never. The tests that hold this
/// up are <c>MarketplaceHttpTransportTests.The_grant_is_never_retried</c> and its accepting control, a GET
/// that is.
/// </para>
/// <para>
/// The transport applies the configured deadline to every attempt itself and turns its expiry into a
/// coded failure. This timeout is the backstop behind it, <see cref="Grace"/> longer, so the transport's
/// deadline always fires first and the pipeline's, which surfaces as a Polly exception the module cannot
/// name, fires only if the transport's has somehow not. The package body is bounded by neither: both
/// end when the response headers arrive, and the downloader applies a stall deadline to every read.
/// </para>
/// </remarks>
public static class MarketplacePipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "marketplace";

    /// <summary>How much longer than the configured deadline the backstop waits.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>Configures the timeout strategy on <paramref name="builder"/>.</summary>
    /// <param name="builder">The HTTP pipeline being built for the named client.</param>
    /// <param name="timeout">
    /// How long one attempt may take, from <c>Marketplace:RequestTimeoutSeconds</c>. The named client's
    /// <see cref="HttpClient.Timeout"/> is infinite, so the configured value, enforced by the transport, is
    /// the value that applies; the strategy added here waits <see cref="Grace"/> beyond it.
    /// </param>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TimeSpan timeout)
    {
        builder.AddTimeout(timeout + Grace);
    }
}
