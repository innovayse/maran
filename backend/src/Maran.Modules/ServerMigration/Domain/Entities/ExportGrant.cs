using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// A single-use credential that lets another panel pull one exported package (issue #127): 256 bits of
/// randomness, of which only the digest is stored, valid for one export and for minutes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consumption rules.</b> The manifest may be read a few times (the importing administrator's preview); the
/// artifact is read once. <see cref="ConsumeArtifact"/> marks the grant at the FIRST byte, so an interrupted
/// pull needs a new grant: a grant half-used is a grant a thief may have half-used. A refusal answers one
/// generic not-found whatever condition failed, so nothing about another tenant's export is confirmed.
/// </para>
/// <para>
/// The digest, not the token, is the row. The token is shown to the issuing administrator once and exists
/// nowhere afterwards (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class ExportGrant
{
    /// <summary>How many times the manifest may be read before the artifact is.</summary>
    public const int MaxManifestReads = 3;

    /// <summary>The length of the hexadecimal SHA-256 digest.</summary>
    public const int DigestLength = 64;

    /// <summary>The longest allowed-from text: the canonical list of ranges, as long as a remote server's.</summary>
    public const int AllowedFromMaxLength = 400;

    /// <summary>The row's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The export the grant opens.</summary>
    public Guid ExportId { get; private set; }

    /// <summary>The lowercase hexadecimal SHA-256 of the token.</summary>
    public string TokenDigest { get; private set; }

    /// <summary>The address range the pull must come from, or <c>null</c> for any.</summary>
    public string? AllowedFrom { get; private set; }

    /// <summary>When the grant stops working.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>How many times the manifest was read.</summary>
    public int ManifestReads { get; private set; }

    /// <summary>When the artifact was first read, or <c>null</c> while it has not been.</summary>
    public DateTimeOffset? ArtifactConsumedAt { get; private set; }

    /// <summary>When an administrator revoked the grant, or <c>null</c>.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>When the grant was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a newly issued grant.</summary>
    /// <param name="id">The row's id.</param>
    /// <param name="exportId">The export the grant opens.</param>
    /// <param name="tokenDigest">The lowercase hexadecimal SHA-256 of the token.</param>
    /// <param name="allowedFrom">The address range the pull must come from, or <c>null</c>.</param>
    /// <param name="createdAt">When the grant was issued.</param>
    /// <param name="lifetime">How long the grant works.</param>
    public ExportGrant(
        Guid id,
        Guid exportId,
        string tokenDigest,
        string? allowedFrom,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        Id = id;
        ExportId = exportId;
        TokenDigest = tokenDigest;
        AllowedFrom = allowedFrom;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + lifetime;
    }

    /// <summary>For EF Core only.</summary>
    private ExportGrant()
    {
        TokenDigest = string.Empty;
    }

    /// <summary>Whether the grant may still open anything.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> while it is not revoked and has not expired.</returns>
    public bool IsLive(DateTimeOffset now)
    {
        return RevokedAt is null && now < ExpiresAt;
    }

    /// <summary>Says where the grant stands.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// Revoked before used before expired: a grant that was read and then ran out of time is <c>Used</c>, because
    /// that is the fact an administrator needs. The list filters by the same order, written as a query in
    /// <c>ListExportGrantsQueryHandler</c>, and a test holds the two to one answer.
    /// </returns>
    public ExportGrantState StateAt(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return ExportGrantState.Revoked;
        }

        if (ArtifactConsumedAt is not null)
        {
            return ExportGrantState.Used;
        }

        return now < ExpiresAt ? ExportGrantState.Live : ExportGrantState.Expired;
    }

    /// <summary>Counts one read of the manifest.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when the read is allowed; <c>false</c> when the grant is not live, the artifact was already read, or the reads are used up.</returns>
    public bool ReadManifest(DateTimeOffset now)
    {
        if (!IsLive(now) || ArtifactConsumedAt is not null || ManifestReads >= MaxManifestReads)
        {
            return false;
        }

        ManifestReads++;

        return true;
    }

    /// <summary>Marks the artifact as read, at the first byte, so a second read is refused.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when this is the first read of a live grant; otherwise <c>false</c> and nothing changes.</returns>
    public bool ConsumeArtifact(DateTimeOffset now)
    {
        if (!IsLive(now) || ArtifactConsumedAt is not null)
        {
            return false;
        }

        ArtifactConsumedAt = now;

        return true;
    }

    /// <summary>Ends the grant before it is used or expires.</summary>
    /// <param name="now">The current time.</param>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
