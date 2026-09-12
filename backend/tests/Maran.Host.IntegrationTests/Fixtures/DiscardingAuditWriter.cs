using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAuditWriter"/> that keeps nothing, for a test whose subject is not the journal.
/// </summary>
/// <remarks>
/// It discards deliberately rather than recording into a list no assertion reads: a counter nothing
/// checks reads as coverage and is not. That a creation writes an audit entry is asserted by the Ftp
/// module's own unit tests, over a writer that records.
/// </remarks>
public sealed class DiscardingAuditWriter : IAuditWriter
{
    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
