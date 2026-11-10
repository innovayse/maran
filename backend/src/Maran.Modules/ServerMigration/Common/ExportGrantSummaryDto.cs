namespace Maran.Modules.ServerMigration.Common;

/// <summary>
/// One pull grant in a list (issue #127): which export it opens, where it stands, and when it ends. Never the token,
/// which was shown once and exists nowhere afterwards.
/// </summary>
/// <param name="Id">The grant's id, with which an administrator revokes it.</param>
/// <param name="ExportId">The export the grant opens.</param>
/// <param name="AccountName">The exported account's system user name.</param>
/// <param name="State">The machine-stable state: <c>Live</c>, <c>Used</c>, <c>Revoked</c> or <c>Expired</c>.</param>
/// <param name="StateName">The state as an operator reads it, in the caller's language.</param>
/// <param name="AllowedFrom">The address range the pull must come from, or <c>null</c> for any.</param>
/// <param name="ManifestReads">How many times the head of the package was read.</param>
/// <param name="CreatedAt">When the grant was issued.</param>
/// <param name="ExpiresAt">When the grant stops working.</param>
/// <param name="ArtifactConsumedAt">When the artifact was read, or <c>null</c>.</param>
/// <param name="RevokedAt">When an administrator revoked it, or <c>null</c>.</param>
public sealed record ExportGrantSummaryDto(
    Guid Id,
    Guid ExportId,
    string AccountName,
    string State,
    string StateName,
    string? AllowedFrom,
    int ManifestReads,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ArtifactConsumedAt,
    DateTimeOffset? RevokedAt);
