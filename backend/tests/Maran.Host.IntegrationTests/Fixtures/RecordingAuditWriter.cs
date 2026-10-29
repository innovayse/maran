using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An audit writer that keeps every entry it is given, so a test can assert that a refusal was
/// journalled rather than only that it was returned.
/// </summary>
/// <remarks>
/// Thread-safe, because the concurrency tests that use it write from two requests at once.
/// </remarks>
public sealed class RecordingAuditWriter : IAuditWriter
{
    /// <summary>The entries written so far, in arrival order.</summary>
    private readonly List<AuditEntry> _entries = [];

    /// <summary>A snapshot of the entries written so far.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }
}
