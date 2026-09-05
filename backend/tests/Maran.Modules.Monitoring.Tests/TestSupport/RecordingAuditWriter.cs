using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>Keeps every audit entry a handler wrote, so a test can assert on the journal.</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Every entry written, in the order it was written.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <summary>Records the entry instead of storing it.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Unused; the journal is in memory.</param>
    /// <returns>A completed task.</returns>
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
