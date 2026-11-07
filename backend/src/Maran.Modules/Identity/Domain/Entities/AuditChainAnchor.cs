namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// Where the audit chain starts now that its oldest rows may have been purged: at most one row, ever.
/// It records the position and hash of the last purged row, so that verification of what is left can
/// begin from a known value instead of from a row that no longer exists.
/// </summary>
/// <remarks>
/// A singleton by construction, as <see cref="SecurityPolicy"/> is. The anchor is advanced in the same
/// transaction as the deletion it describes and before it, so a crash leaves either the old state or
/// the new one, never rows deleted behind an anchor that has not moved.
/// </remarks>
public sealed class AuditChainAnchor
{
    /// <summary>The one primary key this table ever holds.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000004143");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The chain index of the last purged row; 0 when nothing has been purged.</summary>
    public long PurgedThroughIndex { get; private set; }

    /// <summary>
    /// The row hash of the last purged row, which is the previous hash of the first surviving one
    /// (<see cref="AuditEvent.HashLength"/> bytes). For the genesis anchor, the chain's starting value.
    /// </summary>
    public byte[] AnchorHash { get; private set; }

    /// <summary>When the anchor last moved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Parameterless constructor required by EF Core materialization, and used by <see cref="Genesis"/>.</summary>
    private AuditChainAnchor()
    {
        AnchorHash = [];
    }

    /// <summary>Creates the anchor of a chain from which nothing has been purged.</summary>
    /// <param name="anchorHash">The chain's starting value, <see cref="AuditEvent.HashLength"/> bytes; the first row's previous hash.</param>
    /// <param name="at">When the anchor was created, from the panel's clock.</param>
    /// <returns>An anchor at position 0.</returns>
    public static AuditChainAnchor Genesis(byte[] anchorHash, DateTimeOffset at)
    {
        RequireHash(anchorHash);

        return new AuditChainAnchor
        {
            Id = SingletonId,
            PurgedThroughIndex = 0,
            AnchorHash = anchorHash.ToArray(),
            UpdatedAt = at,
        };
    }

    /// <summary>Moves the anchor forward over rows that are about to be purged.</summary>
    /// <param name="through">The chain index of the last row being purged; strictly beyond the current position.</param>
    /// <param name="hash">That row's hash, <see cref="AuditEvent.HashLength"/> bytes.</param>
    /// <param name="at">When the anchor moved, from the panel's clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">The anchor would not move forward.</exception>
    public void Advance(long through, byte[] hash, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(through, PurgedThroughIndex);
        RequireHash(hash);

        PurgedThroughIndex = through;
        AnchorHash = hash.ToArray();
        UpdatedAt = at;
    }

    /// <summary>Refuses a hash that is not exactly <see cref="AuditEvent.HashLength"/> bytes.</summary>
    /// <param name="hash">The hash to check.</param>
    private static void RequireHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if (hash.Length != AuditEvent.HashLength)
        {
            throw new ArgumentException($"A chain hash is exactly {AuditEvent.HashLength} bytes.", nameof(hash));
        }
    }
}
