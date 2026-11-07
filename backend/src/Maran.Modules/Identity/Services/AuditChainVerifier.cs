using System.Security.Cryptography;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Walks the sealed part of the audit journal in chain order and reports the first place where it does
/// not match what the chain key says it should be.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a clean answer means, and what it does not.</b> It means the sealed rows still have the
/// action, outcome, instant and correlation id they were sealed with, still follow each other without a
/// gap, and still have the erasable columns they were sealed with when their salt still exists. It does
/// NOT mean who acted, from where or on what is unaltered once a row has been erased (the salt is gone, so
/// there is nothing to check against), it says nothing of rows not yet sealed, it cannot see rows removed
/// from the newest end, and it cannot tell a retention purge from someone writing the anchor by hand.
/// A rotated panel secret fails at the very first sealed row, exactly as tampering does.
/// </para>
/// <para>
/// <b>One walk at a time.</b> A walk reads every sealed row, so it runs through
/// <see cref="AuditChainVerificationGate"/>: a caller who arrives while a walk runs takes the answer of a
/// walk that starts after their arrival instead of starting a second one.
/// </para>
/// </remarks>
public sealed class AuditChainVerifier
{
    /// <summary>How many rows are read per query.</summary>
    public const int BatchSize = 1000;

    /// <summary>The module's database context, which owns the journal and the chain's anchor.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Derives the chain key from the panel's secret.</summary>
    private readonly ISubKeyDeriver _keyDeriver;

    /// <summary>Keeps two walks from running at once.</summary>
    private readonly AuditChainVerificationGate _gate;

    /// <summary>Creates the verifier.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="keyDeriver">Derives the chain key from the panel's secret.</param>
    /// <param name="gate">Keeps two walks from running at once.</param>
    public AuditChainVerifier(IdentityDbContext dbContext, ISubKeyDeriver keyDeriver, AuditChainVerificationGate gate)
    {
        _dbContext = dbContext;
        _keyDeriver = keyDeriver;
        _gate = gate;
    }

    /// <summary>Verifies the sealed journal from the purge anchor to the newest sealed row.</summary>
    /// <param name="cancellationToken">Cancellation token for the walk.</param>
    /// <returns>The verdict: intact, or the first break found.</returns>
    public async Task<AuditChainVerdict> VerifyAsync(CancellationToken cancellationToken)
    {
        return await _gate.RunAsync(WalkAsync, cancellationToken);
    }

    /// <summary>Does the walk itself, once the gate has admitted it.</summary>
    /// <param name="cancellationToken">Cancellation token for the walk.</param>
    /// <returns>The verdict.</returns>
    private async Task<AuditChainVerdict> WalkAsync(CancellationToken cancellationToken)
    {
        var key = _keyDeriver.DeriveKey(AuditChainHasher.KeyPurpose);

        try
        {
            var anchor = await _dbContext.AuditChainAnchors
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            var lastIndex = anchor?.PurgedThroughIndex ?? 0;
            var previousHash = AuditChainHasher.StartHash(anchor);
            var checkedRows = 0L;

            while (true)
            {
                var rows = await _dbContext.AuditEvents
                    .AsNoTracking()
                    .Where(row => row.ChainIndex > lastIndex)
                    .OrderBy(row => row.ChainIndex)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                foreach (var row in rows)
                {
                    checkedRows++;

                    var problem = Examine(row, lastIndex, previousHash, key, isFirst: checkedRows == 1, anchor);

                    if (problem != AuditChainBreak.None)
                    {
                        return new AuditChainVerdict(false, checkedRows, row.ChainIndex, problem);
                    }

                    lastIndex = row.ChainIndex!.Value;
                    previousHash = row.RowHash!;
                }

                if (rows.Count < BatchSize)
                {
                    return new AuditChainVerdict(true, checkedRows, null, AuditChainBreak.None);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Checks one row against the one before it.</summary>
    /// <param name="row">The sealed row.</param>
    /// <param name="previousIndex">The index of the row (or the anchor) before it.</param>
    /// <param name="previousHash">The hash of the row (or the anchor) before it.</param>
    /// <param name="key">The chain key.</param>
    /// <param name="isFirst">Whether this is the first row examined in the walk.</param>
    /// <param name="anchor">The purge anchor, or <c>null</c> when nothing was ever purged.</param>
    /// <returns><see cref="AuditChainBreak.None"/> when the row is sound; otherwise what is wrong with it.</returns>
    private static AuditChainBreak Examine(
        AuditEvent row,
        long previousIndex,
        byte[] previousHash,
        byte[] key,
        bool isFirst,
        AuditChainAnchor? anchor)
    {
        if (row.ChainIndex != previousIndex + 1)
        {
            return isFirst && anchor is { PurgedThroughIndex: > 0 }
                ? AuditChainBreak.AnchorMismatch
                : AuditChainBreak.IndexGap;
        }

        if (row.ErasableCommitment is null || row.RowHash is null)
        {
            return AuditChainBreak.RowHashMismatch;
        }

        if (row.CommitmentSalt is not null)
        {
            var recomputed = AuditChainHasher.Commitment(row, row.CommitmentSalt);

            if (!AuditChainHasher.Equal(recomputed, row.ErasableCommitment))
            {
                return AuditChainBreak.CommitmentMismatch;
            }
        }
        else if (!row.IsErased)
        {
            return AuditChainBreak.SaltMissing;
        }

        var expected = AuditChainHasher.RowHash(key, previousHash, row, row.ErasableCommitment);

        return AuditChainHasher.Equal(expected, row.RowHash)
            ? AuditChainBreak.None
            : AuditChainBreak.RowHashMismatch;
    }
}
