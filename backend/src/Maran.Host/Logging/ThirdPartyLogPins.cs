using Serilog.Events;

namespace Maran.Host.Logging;

/// <summary>
/// Every third-party namespace whose logging the panel pins, enumerated in one place (issue #337).
/// </summary>
/// <remarks>
/// The panel's runtime level (issue #131) is for the panel's own code. Raising it to Verbose must not
/// also raise what libraries write: Npgsql logs SQL parameters, the HTTP client logs request lines,
/// Wolverine logs message envelopes, and any of them can carry a value the panel never meant to
/// print. Each entry below therefore has a floor of Warning, and a namespace is written at the
/// more severe of its floor and the panel's level — so choosing Error silences a library's warnings
/// too, while choosing Verbose leaves it at Warning.
/// <para>
/// A namespace NOT listed here follows the panel's level: that is the panel's own code, and its
/// libraries are added to this list when they are added to the project. The longest matching entry
/// wins, which is how <c>Microsoft.Hosting.Lifetime</c> keeps its start-up and shutdown lines at
/// Information inside the pinned <c>Microsoft</c> namespace; those lines carry addresses and a
/// content root, never request data.
/// </para>
/// </remarks>
public static class ThirdPartyLogPins
{
    /// <summary>The pinned namespaces.</summary>
    public static readonly IReadOnlyList<ThirdPartyLogPin> All =
    [
        // The message bus and the runtime it is built on: envelopes are logged at Debug.
        new("Wolverine", LogEventLevel.Warning),
        new("JasperFx", LogEventLevel.Warning),
        // The database driver: command text and parameter values.
        new("Npgsql", LogEventLevel.Warning),
        // The agent's gRPC channel.
        new("Grpc", LogEventLevel.Warning),
        // The resilience pipelines' retry and timeout reporting.
        new("Polly", LogEventLevel.Warning),
        // ASP.NET Core, Entity Framework Core, Microsoft.Extensions.Http and the rest of the platform.
        new("Microsoft", LogEventLevel.Warning),
        // The BCL, System.Net.Http.HttpClient among it: request and response lines.
        new("System", LogEventLevel.Warning),
        // The one platform category worth its Information lines.
        new("Microsoft.Hosting.Lifetime", LogEventLevel.Information),
    ];
}
