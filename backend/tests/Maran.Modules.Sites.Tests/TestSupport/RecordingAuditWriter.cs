using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IAuditWriter"/> double that keeps what it was asked to write, so a test can assert
/// that a mutation was journalled and with which subject (rules/testing.md, Definition of Done 4).
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
