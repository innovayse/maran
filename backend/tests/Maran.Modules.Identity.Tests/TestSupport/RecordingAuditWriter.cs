using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IAuditWriter"/> double that keeps what it was asked to write, so a test can assert
/// on the entry itself rather than on a database row — including asserting what the entry does
/// *not* contain.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Every entry this writer was handed, in order.</summary>
    public List<AuditEntry> Written { get; } = [];

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Written.Add(entry);
        return Task.CompletedTask;
    }
}
