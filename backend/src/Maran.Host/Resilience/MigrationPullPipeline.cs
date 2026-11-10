using Maran.Modules.ServerMigration.Domain.Policies;
using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline the receiving panel's pull from another panel goes through (rules/csharp.md "Every
/// outbound call goes through a named resilience pipeline", issue #127). Attached to the named
/// <c>migration-pull</c> <see cref="HttpClient"/> as a handler, so the module obtains a client that is already
/// governed.
/// </summary>
/// <remarks>
/// A TIMEOUT and nothing else, and no retry: a pull spends a single-use grant, and a replay after a read timeout
/// would spend a grant the first attempt may already have used. The module's client applies the header deadline
/// itself and turns its expiry into a coded failure; this is the backstop behind it,
/// <see cref="MigrationPullLimits.Grace"/> longer, so the client's fires first. The body is bounded by neither: both
/// end when the response headers arrive, and the client applies a stall deadline to every read.
/// </remarks>
public static class MigrationPullPipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "migration-pull";

    /// <summary>Configures the timeout strategy on <paramref name="builder"/>.</summary>
    /// <param name="builder">The HTTP pipeline being built for the named client.</param>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddTimeout(MigrationPullLimits.HeaderTimeout + MigrationPullLimits.Grace);
    }
}
