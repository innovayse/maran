using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>
/// Keeps every audit entry a handler wrote, and what the file system looked like at the moment each was
/// written, so a test can assert the order of the journal against the marker.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>What to observe each time an entry is written, or null for nothing.</summary>
    private readonly Func<bool>? _observation;

    /// <summary>Every entry written, in the order it was written.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <summary>The answer of the observation at the moment of each write, in the same order as <see cref="Entries"/>.</summary>
    public List<bool> Observations { get; } = [];

    /// <summary>Creates the writer.</summary>
    /// <param name="observation">Evaluated at each write; its answers are kept in <see cref="Observations"/>.</param>
    public RecordingAuditWriter(Func<bool>? observation = null)
    {
        _observation = observation;
    }

    /// <summary>Records the entry instead of storing it.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Unused; the journal is in memory.</param>
    /// <returns>A completed task.</returns>
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Observations.Add(_observation?.Invoke() ?? false);
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
