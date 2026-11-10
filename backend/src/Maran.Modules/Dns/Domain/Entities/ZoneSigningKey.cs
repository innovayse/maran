using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// The key that signs one <see cref="Zone"/>: its public half, its private scalar encrypted at rest, and the
/// numbers its DNSKEY record carries. It belongs to a customer through its zone, so the tenant filter reaches
/// it by that required relationship and it carries no account id of its own.
/// </summary>
/// <remarks>
/// <para>
/// The row is the panel's copy of the truth: a host that is rebuilt is given the key again from here with
/// every publication of the zone. A zone holds at most one key in this release (no automatic rollover), which
/// a unique index states and the zone's own methods keep.
/// </para>
/// <para>
/// <see cref="PrivateKey"/> holds the plain scalar in memory and ciphertext in the column, through the
/// panel's shared cipher. It is never part of a DTO, an audit entry, a log line or an error.
/// </para>
/// </remarks>
public sealed class ZoneSigningKey
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The zone the key signs.</summary>
    public Guid ZoneId { get; private set; }

    /// <summary>The zone, loaded on demand. The tenant filter of the key is the filter of this zone.</summary>
    public Zone Zone { get; private set; } = null!;

    /// <summary>The public key as standard base64 of the 64 bytes <c>x || y</c>.</summary>
    public string PublicKey { get; private set; }

    /// <summary>The private scalar as standard base64 of its 32 bytes: plain in memory, encrypted in the column.</summary>
    public string PrivateKey { get; private set; }

    /// <summary>The DNSKEY flags: 257 for the combined signing key.</summary>
    public int Flags { get; private set; }

    /// <summary>The DNSSEC algorithm number: 13 for ECDSA P-256.</summary>
    public int Algorithm { get; private set; }

    /// <summary>The RFC 4034 appendix B key tag of the DNSKEY.</summary>
    public int KeyTag { get; private set; }

    /// <summary>The instant the key was generated.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates the row from a generated key. Only <see cref="Zone"/> calls it.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="zoneId">The zone the key signs.</param>
    /// <param name="material">The generated key.</param>
    /// <param name="createdAt">The creation instant, from the clock.</param>
    internal ZoneSigningKey(Guid id, Guid zoneId, DnssecKeyMaterial material, DateTimeOffset createdAt)
    {
        Id = id;
        ZoneId = zoneId;
        PublicKey = material.PublicKey;
        PrivateKey = material.PrivateKey.Reveal();
        Flags = material.Flags;
        Algorithm = material.Algorithm;
        KeyTag = material.KeyTag;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ZoneSigningKey()
    {
        PublicKey = string.Empty;
        PrivateKey = string.Empty;
    }
}
