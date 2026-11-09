using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Demo.Tests.Fixtures;

/// <summary>An audit writer that keeps what it was given, so a test can assert the journal.</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Every entry written, in order.</summary>
    public List<AuditEntry> Entries { get; } = new();

    /// <summary>Remembers the entry.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
