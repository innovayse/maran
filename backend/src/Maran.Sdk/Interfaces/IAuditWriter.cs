using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Appends to the panel's audit journal. The contract lives in the Sdk because every module writes
/// to the same journal and no module may reference the one that owns the table.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Records one event. The journal is append-only: there is no update and no delete.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
