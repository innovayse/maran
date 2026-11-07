using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The cryptography of the audit chain: the commitment to a row's erasable columns, the keyed hash that
/// links a row to the one before it, and the comparison of two such values.
/// </summary>
/// <remarks>
/// <para>
/// <c>RowHash = HMAC-SHA256(key, "maran-audit-v1" ‖ previousRowHash ‖ canon(immutable) ‖ commitment)</c>
/// and <c>commitment = SHA-256(salt ‖ canon(erasable))</c>. The commitment is what lets an erasure change the
/// erasable columns without rewriting the chain: the row hash covers the commitment, not the columns, and
/// destroying the salt makes the old values uncheckable and unguessable from the commitment alone.
/// </para>
/// <para>
/// <b>What this does not do.</b> The row hash does not cover <see cref="AuditEvent.ErasedAt"/> or the
/// erasable columns of an erased row, so a rewrite of those by someone who can write the table is
/// indistinguishable from an erasure. The chain shows that a row with this action, outcome and time exists
/// in this order; it does not show who did it or from where once the salt is gone. Only primitives from the
/// base class library are used.
/// </para>
/// </remarks>
public static class AuditChainHasher
{
    /// <summary>The purpose label the chain key is derived under; rotating the panel secret changes the key.</summary>
    public const string KeyPurpose = "maran/audit-chain/v1";

    /// <summary>The domain-separation label every row hash starts with.</summary>
    private const string RowHashLabel = "maran-audit-v1";

    /// <summary>The label the hash standing in for "no previous row" is taken over.</summary>
    private const string GenesisLabel = "maran-audit-genesis-v1";

    /// <summary>The previous-row hash of the first row of a chain that has never been purged.</summary>
    /// <remarks>A new array on every read, so no caller can alter the value another caller sees.</remarks>
    public static byte[] GenesisHash
    {
        get
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(GenesisLabel));
        }
    }

    /// <summary>Creates a random salt for one row's commitment.</summary>
    /// <returns>A new salt of <see cref="AuditEvent.SaltLength"/> bytes.</returns>
    public static byte[] NewSalt()
    {
        return RandomNumberGenerator.GetBytes(AuditEvent.SaltLength);
    }

    /// <summary>The hash the first surviving row is linked to.</summary>
    /// <param name="anchor">The record of the purged prefix, or <c>null</c> when none was ever recorded.</param>
    /// <returns>
    /// The hash of the last purged row, or <see cref="GenesisHash"/> when nothing was purged. The anchor's
    /// own hash is trusted only when it says something was purged: an anchor through index zero is the
    /// genesis, whatever bytes it holds.
    /// </returns>
    public static byte[] StartHash(AuditChainAnchor? anchor)
    {
        if (anchor is null || anchor.PurgedThroughIndex <= 0)
        {
            return GenesisHash;
        }

        return [.. anchor.AnchorHash];
    }

    /// <summary>Commits to a row's erasable columns.</summary>
    /// <param name="row">The row whose erasable columns are committed to.</param>
    /// <param name="salt">The row's salt, or <c>null</c> for a row that was already erased when it was sealed.</param>
    /// <returns>The commitment, <see cref="AuditEvent.HashLength"/> bytes.</returns>
    public static byte[] Commitment(AuditEvent row, byte[]? salt)
    {
        ArgumentNullException.ThrowIfNull(row);

        byte[] input = [.. salt ?? Array.Empty<byte>(), .. AuditCanonicalForm.Erasable(row)];

        return SHA256.HashData(input);
    }

    /// <summary>Links a row to the one before it under the chain key.</summary>
    /// <param name="key">The chain key.</param>
    /// <param name="previousHash">The previous row's hash, or the start hash for the first row.</param>
    /// <param name="row">The row being linked; only its immutable columns are read.</param>
    /// <param name="commitment">The row's commitment to its erasable columns.</param>
    /// <returns>The row hash, <see cref="AuditEvent.HashLength"/> bytes.</returns>
    public static byte[] RowHash(byte[] key, byte[] previousHash, AuditEvent row, byte[] commitment)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(previousHash);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(commitment);

        byte[] input =
        [
            .. Encoding.UTF8.GetBytes(RowHashLabel),
            .. previousHash,
            .. AuditCanonicalForm.Immutable(row),
            .. commitment,
        ];

        return HMACSHA256.HashData(key, input);
    }

    /// <summary>Compares two hashes without revealing, by timing, how many leading bytes agree.</summary>
    /// <param name="left">One value.</param>
    /// <param name="right">The other value.</param>
    /// <returns><c>true</c> when both are the same length and the same bytes.</returns>
    public static bool Equal(byte[] left, byte[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
