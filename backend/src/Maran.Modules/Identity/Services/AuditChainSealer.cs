using System.Security.Cryptography;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Gives audit entries that have no place in the tamper-evidence chain their place, oldest first, each
/// linked to the one before it under the chain key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sealing runs apart from the write path.</b> The audit writer never takes a lock and never touches
/// the chain, so an entry is unsealed from the moment it is written until the next maintenance tick. In
/// that window it is not protected; the status endpoint reports how many entries are in it.
/// </para>
/// <para>
/// <b>Order is the entry's instant, then its id.</b> The index a row receives is the order the chain is
/// walked in, so two sealing runs, or a sealing run and a verification, always agree on it. A unique index
/// on the chain index makes a second sealer that raced this one fail on save rather than fork the chain;
/// the failure is for the caller to log and retry on the next tick.
/// </para>
/// <para>
/// The caller owns the unit of work: this type saves once per batch on the context it was given and
/// detaches the rows it sealed, so a long backlog does not stay tracked in memory.
/// </para>
/// </remarks>
public sealed class AuditChainSealer
{
    /// <summary>How many entries are sealed and saved together.</summary>
    public const int BatchSize = 500;

    /// <summary>How many batches one run seals at most, so a long backlog is spread over several ticks.</summary>
    public const int MaxBatchesPerRun = 20;

    /// <summary>The module's database context, which owns the journal and the chain's anchor.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Derives the chain key from the panel's secret.</summary>
    private readonly ISubKeyDeriver _keyDeriver;

    /// <summary>Creates the sealer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="keyDeriver">Derives the chain key from the panel's secret.</param>
    public AuditChainSealer(IdentityDbContext dbContext, ISubKeyDeriver keyDeriver)
    {
        _dbContext = dbContext;
        _keyDeriver = keyDeriver;
    }

    /// <summary>Seals up to <paramref name="maxBatches"/> batches of unsealed entries.</summary>
    /// <param name="maxBatches">The most batches of <see cref="BatchSize"/> this run may seal; at least one.</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    /// <returns>How many entries were sealed; zero when there was nothing to seal.</returns>
    public async Task<int> SealAsync(int maxBatches, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatches);

        var key = _keyDeriver.DeriveKey(AuditChainHasher.KeyPurpose);

        try
        {
            var (lastIndex, lastHash) = await ReadTailAsync(cancellationToken);
            var sealedTotal = 0;

            for (var batch = 0; batch < maxBatches; batch++)
            {
                var rows = await _dbContext.AuditEvents
                    .Where(row => row.ChainIndex == null)
                    .OrderBy(row => row.OccurredAt)
                    .ThenBy(row => row.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                foreach (var row in rows)
                {
                    lastIndex++;

                    var salt = row.IsErased ? null : AuditChainHasher.NewSalt();
                    var commitment = AuditChainHasher.Commitment(row, salt);
                    lastHash = AuditChainHasher.RowHash(key, lastHash, row, commitment);

                    row.Seal(lastIndex, commitment, salt, lastHash);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                foreach (var row in rows)
                {
                    _dbContext.Entry(row).State = EntityState.Detached;
                }

                sealedTotal += rows.Count;

                if (rows.Count < BatchSize)
                {
                    break;
                }
            }

            return sealedTotal;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Finds where the chain currently ends: the newest sealed row, else the purge anchor, else the start.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>The index and hash the next row is linked to.</returns>
    private async Task<(long Index, byte[] Hash)> ReadTailAsync(CancellationToken cancellationToken)
    {
        var newest = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(row => row.ChainIndex != null)
            .OrderByDescending(row => row.ChainIndex)
            .Select(row => new { row.ChainIndex, row.RowHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (newest is not null)
        {
            return (
                newest.ChainIndex!.Value,
                newest.RowHash ?? throw new InvalidOperationException("A sealed audit row has no row hash."));
        }

        var anchor = await _dbContext.AuditChainAnchors
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return (anchor?.PurgedThroughIndex ?? 0, AuditChainHasher.StartHash(anchor));
    }
}
