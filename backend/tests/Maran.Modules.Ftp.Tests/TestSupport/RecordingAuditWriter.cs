using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IAuditWriter"/> that keeps every entry in memory, so a test can read what the
/// journal recorded — successes and refusals alike.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Every entry written, in the order it was written.</summary>
    private readonly List<AuditEntry> _entries = [];

    /// <summary>Every entry written, in the order it was written.</summary>
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
