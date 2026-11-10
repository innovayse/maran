namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// A grant joined with the name of the account its export belongs to, so the list searches and sorts by that name in
/// the database (issue #127). Read-only data the list handler passes to the pager.
/// </summary>
public sealed class ExportGrantRow
{
    /// <summary>The grant's id.</summary>
    public Guid Id { get; init; }

    /// <summary>The export the grant opens.</summary>
    public Guid ExportId { get; init; }

    /// <summary>The exported account's system user name.</summary>
    public string AccountName { get; init; } = string.Empty;

    /// <summary>The address range the pull must come from, or <c>null</c>.</summary>
    public string? AllowedFrom { get; init; }

    /// <summary>How many times the head was read.</summary>
    public int ManifestReads { get; init; }

    /// <summary>When the grant was issued.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the grant stops working.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the artifact was read, or <c>null</c>.</summary>
    public DateTimeOffset? ArtifactConsumedAt { get; init; }

    /// <summary>When the grant was revoked, or <c>null</c>.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
