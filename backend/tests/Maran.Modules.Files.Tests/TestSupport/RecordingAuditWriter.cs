using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>An audit writer that keeps every entry it was given, in order.</summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>Gets the entries written so far.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
