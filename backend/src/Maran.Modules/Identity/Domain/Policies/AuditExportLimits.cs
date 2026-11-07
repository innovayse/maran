namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The bounds on one export of the audit journal: how many rows a single file may hold and how many
/// rows are read from the database at a time. Stateless, so the handler that refuses an oversized
/// export and the writer that streams it read the same numbers.
/// </summary>
public static class AuditExportLimits
{
    /// <summary>
    /// The most rows one export may hold. An export above it is refused, never truncated: a file that
    /// silently stopped short would read as the complete record.
    /// </summary>
    public const int MaxRows = 500_000;

    /// <summary>How many rows the writer reads and sends before it flushes the response.</summary>
    public const int BatchSize = 1000;
}
