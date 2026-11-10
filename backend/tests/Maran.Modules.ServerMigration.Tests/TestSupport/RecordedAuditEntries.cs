using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>An audit writer that keeps every entry it is given, so a test can assert what the journal recorded.</summary>
public sealed class RecordedAuditEntries : IAuditWriter
{
    /// <summary>The entries written so far, in order.</summary>
    private readonly List<AuditEntry> _entries = [];

    /// <summary>The entries written so far, in order.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get { return _entries; }
    }

    /// <summary>Keeps the entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}
