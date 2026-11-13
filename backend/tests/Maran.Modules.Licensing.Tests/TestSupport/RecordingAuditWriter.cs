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

    /// <summary>When set, every write after this many have been stored throws, as a store that went down would.</summary>
    public int? FailAfterWrites { get; set; }

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        if (FailAfterWrites is { } limit && Entries.Count >= limit)
        {
            throw new InvalidOperationException("The journal is down.");
        }

        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
