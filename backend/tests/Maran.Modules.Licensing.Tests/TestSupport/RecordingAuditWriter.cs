using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// An <see cref="IAuditWriter"/> double that keeps what it was asked to write, so a test can assert
/// that an install attempt was journalled and with which subject.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Everything this writer was handed, in order.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
