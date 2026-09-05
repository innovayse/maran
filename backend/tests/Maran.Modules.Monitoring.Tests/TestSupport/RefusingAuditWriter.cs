using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>
/// A journal that always fails, standing in for the PostgreSQL the real one reaches.
/// </summary>
/// <remarks>
/// It exists for one test: the mail handler must not let an exception escape even when the thing it
/// uses to RECORD failures is itself the thing that failed. Without a double that raises, the
/// handler's inner try/catch is unreachable code that no test distinguishes from its absence.
/// </remarks>
public sealed class RefusingAuditWriter : IAuditWriter
{
    /// <summary>Throws instead of recording.</summary>
    /// <param name="entry">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("the journal is unreachable");
    }
}
