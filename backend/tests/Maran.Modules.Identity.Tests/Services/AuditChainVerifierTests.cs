using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the walk along the audit chain. Every refusal is paired with an acceptance of
/// the clean chain it was cut from, so a verifier that refused everything would fail here.
/// </summary>
public sealed class AuditChainVerifierTests : IDisposable
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<AuditChainVerdict> VerifyAsync(string secret = AuditChainFixture.Secret)
    {
        return await AuditChainFixture.Verifier(_context, secret).VerifyAsync(CancellationToken.None);
    }

    /// <summary>A chain that has just been sealed is accepted (the inverse control for every refusal below).</summary>
    [Fact]
    public async Task A_freshly_sealed_chain_is_intact()
    {
        await AuditChainFixture.SeedSealedAsync(_context, 5);

        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(5, verdict.RowsChecked);
        Assert.Null(verdict.FirstBrokenChainIndex);
        Assert.Equal(AuditChainBreak.None, verdict.Break);
    }

    /// <summary>An empty journal has nothing to be wrong with.</summary>
    [Fact]
    public async Task An_empty_journal_is_intact()
    {
        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(0, verdict.RowsChecked);
    }

    /// <summary>A chain longer than one read is walked to its end.</summary>
    [Fact]
    public async Task A_chain_longer_than_one_batch_is_walked_to_its_end()
    {
        await AuditChainFixture.SeedSealedAsync(_context, AuditChainVerifier.BatchSize + 5);

        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(AuditChainVerifier.BatchSize + 5, verdict.RowsChecked);
    }

    /// <summary>Rows not yet sealed are outside the walk: they are neither checked nor a fault.</summary>
    [Fact]
    public async Task Unsealed_rows_are_not_part_of_the_walk()
    {
        await AuditChainFixture.SeedSealedAsync(_context, 2);
        await AuditChainFixture.SeedAsync(_context, 3);

        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(2, verdict.RowsChecked);
    }

    /// <summary>A changed immutable column is found at the row that changed (the positive control).</summary>
    [Fact]
    public async Task A_changed_action_is_found_at_the_row_that_changed()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 4);
        _context.Entry(rows[2]).Property(row => row.Action).CurrentValue = "Logout";
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(3, verdict.FirstBrokenChainIndex);
        Assert.Equal(3, verdict.RowsChecked);
    }

    /// <summary>A changed outcome is found as well: the outcome is part of what the row hash covers.</summary>
    [Fact]
    public async Task A_changed_outcome_is_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 2);
        _context.Entry(rows[0]).Property(row => row.Succeeded).CurrentValue = false;
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(1, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A changed erasable column of a row that still has its salt is found by its commitment.</summary>
    [Fact]
    public async Task A_changed_subject_of_a_row_that_still_has_its_salt_is_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 3);
        _context.Entry(rows[1]).Property(row => row.Subject).CurrentValue = "somebody else";
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.CommitmentMismatch, verdict.Break);
        Assert.Equal(2, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A row removed from the middle leaves a gap, found at the row after it.</summary>
    [Fact]
    public async Task A_row_removed_from_the_middle_is_found_as_a_gap()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 5);
        _context.AuditEvents.Remove(rows[2]);
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.IndexGap, verdict.Break);
        Assert.Equal(4, verdict.FirstBrokenChainIndex);
    }

    /// <summary>Two rows that swap places are found, because each was linked to the one that used to precede it.</summary>
    [Fact]
    public async Task Two_rows_that_swap_places_are_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 3);
        _context.Entry(rows[0]).Property(row => row.ChainIndex).CurrentValue = 2L;
        _context.Entry(rows[1]).Property(row => row.ChainIndex).CurrentValue = 1L;
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(1, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A sealed row with neither a salt nor an erasure mark is found: its commitment can no longer be checked.</summary>
    [Fact]
    public async Task A_row_with_no_salt_and_no_erasure_mark_is_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 2);
        _context.Entry(rows[1]).Property(row => row.CommitmentSalt).CurrentValue = null;
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.SaltMissing, verdict.Break);
        Assert.Equal(2, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A properly erased row keeps the chain intact, even though its erasable columns now say something else.</summary>
    [Fact]
    public async Task An_erased_row_leaves_the_chain_intact()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 4);
        rows[1].EraseAccount(AuditChainFixture.Start.AddDays(1), Guid.NewGuid(), "erased-account-1a2b3c4d");
        rows[2].ErasePerson(
            rows[2].ActorUserId!.Value,
            AuditChainFixture.Start.AddDays(1),
            Guid.NewGuid(),
            "erased-user-1a2b3c4d",
            new HashSet<string>());
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(4, verdict.RowsChecked);
        Assert.Null(rows[1].CommitmentSalt);
    }

    /// <summary>Erasing a row does not hide a change to an immutable column of the same row.</summary>
    [Fact]
    public async Task An_erased_row_with_a_changed_action_is_still_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 3);
        rows[1].EraseAccount(AuditChainFixture.Start.AddDays(1), Guid.NewGuid(), "erased-account-1a2b3c4d");
        _context.Entry(rows[1]).Property(row => row.Action).CurrentValue = "Logout";
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(2, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A chain whose oldest rows were purged verifies from the anchor.</summary>
    [Fact]
    public async Task A_chain_with_a_purged_prefix_is_intact_from_its_anchor()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 5);
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(2, rows[1].RowHash!, AuditChainFixture.Start);
        _context.AuditChainAnchors.Add(anchor);
        _context.AuditEvents.RemoveRange(rows[0], rows[1]);
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(3, verdict.RowsChecked);
    }

    /// <summary>An anchor that does not hold the hash of the last purged row is found at the first row left.</summary>
    [Fact]
    public async Task An_anchor_with_the_wrong_hash_is_found_at_the_first_surviving_row()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 4);
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(2, rows[0].RowHash!, AuditChainFixture.Start);
        _context.AuditChainAnchors.Add(anchor);
        _context.AuditEvents.RemoveRange(rows[0], rows[1]);
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(3, verdict.FirstBrokenChainIndex);
    }

    /// <summary>An anchor that claims more was purged than was, leaves the first surviving row out of step with it.</summary>
    [Fact]
    public async Task An_anchor_that_does_not_meet_the_first_surviving_row_is_found()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 4);
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(1, rows[0].RowHash!, AuditChainFixture.Start);
        _context.AuditChainAnchors.Add(anchor);
        _context.AuditEvents.RemoveRange(rows[0], rows[1]);
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.AnchorMismatch, verdict.Break);
        Assert.Equal(3, verdict.FirstBrokenChainIndex);
    }

    /// <summary>Without an anchor, a journal whose first sealed row is not number one has lost its head.</summary>
    [Fact]
    public async Task A_missing_head_without_an_anchor_is_found_as_a_gap()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 3);
        _context.AuditEvents.Remove(rows[0]);
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.IndexGap, verdict.Break);
        Assert.Equal(2, verdict.FirstBrokenChainIndex);
    }

    /// <summary>A panel secret that is not the one the chain was sealed under breaks it at the very first row.</summary>
    /// <remarks>
    /// The same as tampering, by design and by honest limit: nothing on the row says which key it was
    /// sealed under, so a rotated secret and an attack read alike.
    /// </remarks>
    [Fact]
    public async Task A_different_panel_secret_breaks_the_chain_at_the_first_row()
    {
        await AuditChainFixture.SeedSealedAsync(_context, 3);

        var verdict = await VerifyAsync(AuditChainFixture.OtherSecret);

        Assert.False(verdict.Intact);
        Assert.Equal(AuditChainBreak.RowHashMismatch, verdict.Break);
        Assert.Equal(1, verdict.FirstBrokenChainIndex);
    }

    /// <summary>The walk stops at the first break and does not count what lies beyond it.</summary>
    [Fact]
    public async Task The_walk_stops_at_the_first_break()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 6);
        _context.Entry(rows[1]).Property(row => row.Action).CurrentValue = "Logout";
        _context.Entry(rows[4]).Property(row => row.Action).CurrentValue = "Logout";
        await _context.SaveChangesAsync();

        var verdict = await VerifyAsync();

        Assert.Equal(2, verdict.FirstBrokenChainIndex);
        Assert.Equal(2, verdict.RowsChecked);
    }
}
