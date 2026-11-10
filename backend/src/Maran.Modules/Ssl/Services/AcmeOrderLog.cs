using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>The one log line an order that failed at a stage writes, shared by every step of the order.</summary>
internal static class AcmeOrderLog
{
    /// <summary>Pre-compiled log delegate for a failed order, so a renewal pass logs cheaply.</summary>
    /// <remarks>
    /// The third placeholder is the panel's own error CODE, and it is named so. An earlier version
    /// called it <c>AuthorityText</c> while passing the same code, which promised an operator a
    /// description the line could never contain. The authority's own machine fields — status and
    /// problem type — are logged by <see cref="AcmeSession"/>, which is the layer that has them.
    /// </remarks>
    private static readonly Action<ILogger, string, string, string, Exception?> LogOrderFailure =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AcmeClient)),
            "ACME order for {Domain} failed at {Stage} with {ErrorCode}");

    /// <summary>Logs one failed stage.</summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="domain">The domain the order was for.</param>
    /// <param name="stage">Which step refused, so an operator can tell an unreachable authority from a rejected order.</param>
    /// <param name="code">The panel's own error code, or the type name of an exception.</param>
    public static void Failed(ILogger logger, string domain, string stage, string code)
    {
        LogOrderFailure(logger, domain, stage, code, null);
    }
}
