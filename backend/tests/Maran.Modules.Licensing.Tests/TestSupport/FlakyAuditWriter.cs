using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>An audit writer that stores entries while it works and throws while the test says the journal is down.</summary>
internal sealed class FlakyAuditWriter : IAuditWriter
{
    /// <summary>The entries stored while the writer worked.</summary>
    internal List<AuditEntry> Entries { get; } = [];

    /// <summary>Whether a write fails instead of storing.</summary>
    internal bool Down { get; set; }

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new InvalidOperationException("The journal is down.");
        }

        Entries.Add(entry);

        return Task.CompletedTask;
    }
}
