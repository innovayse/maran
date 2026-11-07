using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the service that gives audit entries their place in the chain.</summary>
public sealed class AuditChainSealerTests : IDisposable
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private AuditChainSealer NewSealer()
    {
        return new AuditChainSealer(_context, AuditChainFixture.Deriver());
    }

    private static byte[] Key()
    {
        return AuditChainFixture.Deriver().DeriveKey(AuditChainHasher.KeyPurpose);
    }

    /// <summary>The first rows are numbered from one, oldest first, and linked from the genesis hash.</summary>
    [Fact]
    public async Task The_first_rows_are_numbered_from_one_oldest_first_and_linked_from_the_genesis_hash()
    {
        var rows = await AuditChainFixture.SeedAsync(_context, 3);

        var sealedCount = await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(3, sealedCount);
        Assert.Equal(new long[] { 1, 2, 3 }, rows.Select(row =>
        {
            return row.ChainIndex!.Value;
        }));

        var key = Key();
        var previous = AuditChainHasher.GenesisHash;

        foreach (var row in rows)
        {
            var expected = AuditChainHasher.RowHash(key, previous, row, row.ErasableCommitment!);

            Assert.Equal(expected, row.RowHash);
            Assert.Equal(row.ErasableCommitment, AuditChainHasher.Commitment(row, row.CommitmentSalt));
            Assert.Equal(AuditEvent.SaltLength, row.CommitmentSalt!.Length);

            previous = row.RowHash!;
        }
    }

    /// <summary>Rows written out of order are still numbered by when they happened.</summary>
    [Fact]
    public async Task Rows_are_numbered_by_when_they_happened_and_not_by_when_they_were_stored()
    {
        var later = AuditChainFixture.Row(AuditChainFixture.Start.AddHours(1), subject: "later");
        var earlier = AuditChainFixture.Row(AuditChainFixture.Start, subject: "earlier");
        _context.AuditEvents.AddRange(later, earlier);
        await _context.SaveChangesAsync();

        await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(1, earlier.ChainIndex);
        Assert.Equal(2, later.ChainIndex);
    }

    /// <summary>Two rows at the same instant are ordered by id, so the order never depends on storage.</summary>
    [Fact]
    public async Task Rows_at_the_same_instant_are_ordered_by_id()
    {
        var low = AuditChainFixture.Row(id: new Guid("00000000-0000-0000-0000-000000000001"));
        var high = AuditChainFixture.Row(id: new Guid("ffffffff-0000-0000-0000-000000000001"));
        _context.AuditEvents.AddRange(high, low);
        await _context.SaveChangesAsync();

        await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(1, low.ChainIndex);
        Assert.Equal(2, high.ChainIndex);
    }

    /// <summary>A later run continues from the newest sealed row and leaves the sealed rows alone.</summary>
    [Fact]
    public async Task A_later_run_continues_from_the_newest_sealed_row()
    {
        var first = await AuditChainFixture.SeedSealedAsync(_context, 2);
        var firstHashes = first.Select(row =>
        {
            return row.RowHash!.ToArray();
        }).ToList();

        var more = Enumerable.Range(2, 2)
            .Select(index =>
            {
                return AuditChainFixture.Row(AuditChainFixture.Start.AddMinutes(index));
            })
            .ToList();
        _context.AuditEvents.AddRange(more);
        await _context.SaveChangesAsync();

        var sealedCount = await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(2, sealedCount);
        Assert.Equal(new long[] { 3, 4 }, more.Select(row =>
        {
            return row.ChainIndex!.Value;
        }));
        Assert.Equal(
            AuditChainHasher.RowHash(Key(), first[1].RowHash!, more[0], more[0].ErasableCommitment!),
            more[0].RowHash);
        Assert.Equal(firstHashes[0], first[0].RowHash);
        Assert.Equal(firstHashes[1], first[1].RowHash);
    }

    /// <summary>With nothing left to seal, a run seals nothing and changes nothing (the inverse control).</summary>
    [Fact]
    public async Task A_run_with_nothing_unsealed_seals_nothing()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 2);
        var hashes = rows.Select(row =>
        {
            return row.RowHash!.ToArray();
        }).ToList();

        var sealedCount = await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(0, sealedCount);
        Assert.Equal(hashes[0], rows[0].RowHash);
        Assert.Equal(hashes[1], rows[1].RowHash);
    }

    /// <summary>With every row purged, sealing continues from the anchor and not from the genesis.</summary>
    [Fact]
    public async Task With_no_sealed_rows_left_sealing_continues_from_the_anchor()
    {
        var anchorHash = Enumerable.Range(7, AuditEvent.HashLength).Select(value =>
        {
            return (byte)value;
        }).ToArray();
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(10, anchorHash, AuditChainFixture.Start);
        _context.AuditChainAnchors.Add(anchor);
        var rows = await AuditChainFixture.SeedAsync(_context, 1);

        await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Equal(11, rows[0].ChainIndex);
        Assert.Equal(
            AuditChainHasher.RowHash(Key(), anchorHash, rows[0], rows[0].ErasableCommitment!),
            rows[0].RowHash);
    }

    /// <summary>A row erased before it is sealed is sealed without a salt, and the others keep theirs.</summary>
    [Fact]
    public async Task A_row_erased_before_sealing_gets_no_salt_and_the_others_do()
    {
        var rows = await AuditChainFixture.SeedAsync(_context, 2);
        rows[0].EraseAccount(AuditChainFixture.Start, Guid.NewGuid(), "erased-account-1a2b3c4d");
        await _context.SaveChangesAsync();

        await NewSealer().SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        Assert.Null(rows[0].CommitmentSalt);
        Assert.NotNull(rows[1].CommitmentSalt);
    }

    /// <summary>A run seals at most the number of batches it was given, and the rest wait for the next one.</summary>
    [Fact]
    public async Task A_run_seals_at_most_the_batches_it_was_given()
    {
        var rows = await AuditChainFixture.SeedAsync(_context, AuditChainSealer.BatchSize + 1);

        var sealedCount = await NewSealer().SealAsync(1, CancellationToken.None);

        Assert.Equal(AuditChainSealer.BatchSize, sealedCount);
        Assert.Single(rows, row =>
        {
            return !row.IsSealed;
        });

        var rest = await NewSealer().SealAsync(1, CancellationToken.None);

        Assert.Equal(1, rest);
        Assert.All(rows, row =>
        {
            Assert.True(row.IsSealed);
        });
    }

    /// <summary>A run that may seal no batch at all is refused rather than quietly doing nothing.</summary>
    [Fact]
    public async Task A_run_allowed_no_batches_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
            {
                return NewSealer().SealAsync(0, CancellationToken.None);
            });
    }

    /// <summary>Sealing under another panel secret produces another chain.</summary>
    [Fact]
    public async Task Sealing_under_another_panel_secret_produces_other_hashes()
    {
        using var other = IdentityTestContext.Create();
        var mine = await AuditChainFixture.SeedSealedAsync(_context, 1);
        var theirs = await AuditChainFixture.SeedSealedAsync(other, 1, AuditChainFixture.OtherSecret);

        var sameRowOtherKey = AuditChainHasher.RowHash(
            AuditChainFixture.Deriver(AuditChainFixture.OtherSecret).DeriveKey(AuditChainHasher.KeyPurpose),
            AuditChainHasher.GenesisHash,
            mine[0],
            mine[0].ErasableCommitment!);

        Assert.NotEqual(mine[0].RowHash, sameRowOtherKey);
        Assert.Equal(1, theirs[0].ChainIndex);
    }
}
