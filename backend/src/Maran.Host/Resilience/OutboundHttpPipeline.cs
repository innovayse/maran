using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline a call from this panel to an Innovayse service goes through when no module-specific
/// pipeline exists for it (rules/csharp.md "Every outbound call goes through a named resilience pipeline"):
/// today the licence lease renewal. Attached to the module's named <see cref="HttpClient"/> as a handler, so
/// the module obtains a client that is already governed and cannot accidentally make an ungoverned call.
/// </summary>
/// <remarks>
/// <para>
/// It is a TIMEOUT and nothing else, and the absence of a retry is the design. The renewal runs once a day and
/// asks again hours later when it fails (<c>LeaseRenewalPolicy</c>), so a retry here would only add a burst on
/// a service that is already struggling, inside a call nobody is waiting on. The licence service's answer to a
/// retried request is the same lease, but a retry policy is a decision about THAT service's load, which is the
/// scheduler's to make with jitter, not a library default's.
/// </para>
/// <para>
/// The timeout is a small grace above the deadline the options name, because the client's own handler has a
/// connect timeout and the deadline is meant to bound the whole attempt, not to race it.
/// </para>
/// </remarks>
public static class OutboundHttpPipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "outbound-http";

    /// <summary>Added to the configured deadline so the attempt's own end, not the pipeline's, is what the caller normally sees.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>Configures the timeout strategy on <paramref name="builder"/>.</summary>
    /// <param name="builder">The HTTP pipeline being built for the named client.</param>
    /// <param name="timeout">How long one call may take, from the module's options.</param>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TimeSpan timeout)
    {
        builder.AddTimeout(timeout + Grace);
    }
}
