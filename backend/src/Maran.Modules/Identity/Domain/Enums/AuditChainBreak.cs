namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// What the audit chain verifier found wrong at the first row it could not accept. One value per
/// kind of disagreement, so the verdict says which check failed rather than only that one did.
/// </summary>
public enum AuditChainBreak
{
    /// <summary>The chain is intact; nothing was found wrong.</summary>
    None,

    /// <summary>A row's stored hash differs from the hash recomputed over its immutable columns and its predecessor.</summary>
    RowHashMismatch,

    /// <summary>A row that still holds its salt has erasable columns that no longer match their commitment.</summary>
    CommitmentMismatch,

    /// <summary>The chain indexes are not consecutive: a row inside the sealed range is missing.</summary>
    IndexGap,

    /// <summary>The first surviving row does not follow from the anchor left by the last purge.</summary>
    AnchorMismatch,

    /// <summary>A sealed row has neither a salt nor an erasure mark, so nothing explains why its commitment cannot be checked.</summary>
    SaltMissing,
}
