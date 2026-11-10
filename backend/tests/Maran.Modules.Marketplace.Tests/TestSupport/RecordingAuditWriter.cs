using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>An <see cref="IAuditWriter"/> that keeps every entry it is given, in order.</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>The entries written so far.</summary>
    private readonly List<AuditEntry> _entries = [];

    /// <summary>The entries written so far, oldest first.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            return _entries;
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}
