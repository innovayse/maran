using Serilog.Events;

namespace Maran.Host.Logging;

/// <summary>
/// One namespace the panel does not own, and the lowest level it may ever be logged at (issue #337).
/// </summary>
/// <param name="Namespace">
/// The namespace prefix, matched on a name boundary: <c>Npgsql</c> covers <c>Npgsql.Command</c>, never
/// <c>Npgsqlx</c>.
/// </param>
/// <param name="Floor">The most verbose level this namespace is allowed, whatever the panel's level is.</param>
public sealed record ThirdPartyLogPin(string Namespace, LogEventLevel Floor)
{
    /// <summary>Says whether a logger's source context belongs to this pin's namespace.</summary>
    /// <param name="sourceContext">
    /// The <c>SourceContext</c> of a log event: the name of the type or category that wrote it.
    /// </param>
    /// <returns>True when the context is the namespace itself or sits beneath it.</returns>
    public bool Covers(string sourceContext)
    {
        return sourceContext.Equals(Namespace, StringComparison.Ordinal)
            || sourceContext.StartsWith(Namespace + ".", StringComparison.Ordinal);
    }
}
